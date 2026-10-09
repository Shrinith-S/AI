using Chitra.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Infrastructure;

/// <summary>Turns application errors into RFC 7807 problem responses with a machine-readable "code".</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, detail) = exception switch
        {
            AppException app => (StatusFor(app.Code), app.Code.ToString(), TitleFor(app.Code), app.Message),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "ClientClosedRequest", "Request cancelled", "The request was cancelled."),
            BadHttpRequestException bad => (bad.StatusCode, "BadRequest", "Bad request", bad.Message),
            _ => (StatusCodes.Status500InternalServerError, "InternalError", "Unexpected error",
                "An unexpected error occurred. Check the API logs for details."),
        };

        if (status >= 500 && exception is not AppException)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Type = $"https://chitra.local/errors/{code}" };
        problem.Extensions["code"] = code;
        if (exception is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static int StatusFor(ErrorCode code) => code switch
    {
        ErrorCode.Validation => StatusCodes.Status400BadRequest,
        ErrorCode.NotFound => StatusCodes.Status404NotFound,
        ErrorCode.LocalOnlyViolation => StatusCodes.Status403Forbidden,
        ErrorCode.Conflict or ErrorCode.Cancelled => StatusCodes.Status409Conflict,
        ErrorCode.ModelMissing or ErrorCode.InsufficientVram or ErrorCode.InvalidWorkflow => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.ProviderUnavailable or ErrorCode.FfmpegUnavailable or ErrorCode.TtsUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCode.DiskFull => StatusCodes.Status507InsufficientStorage,
        ErrorCode.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status502BadGateway,
    };

    private static string TitleFor(ErrorCode code) => code switch
    {
        ErrorCode.Validation => "Invalid request",
        ErrorCode.NotFound => "Not found",
        ErrorCode.ProviderUnavailable => "AI engine unavailable",
        ErrorCode.ModelMissing => "Model not installed",
        ErrorCode.InsufficientVram => "Not enough GPU memory",
        ErrorCode.InvalidWorkflow => "Invalid workflow",
        ErrorCode.FfmpegUnavailable => "FFmpeg unavailable",
        ErrorCode.TtsUnavailable => "Text-to-speech unavailable",
        ErrorCode.DiskFull => "Disk full",
        ErrorCode.Timeout => "Timed out",
        ErrorCode.Cancelled => "Cancelled",
        ErrorCode.LocalOnlyViolation => "Blocked by Local-Only mode",
        ErrorCode.Conflict => "Conflict",
        _ => "Generation failed",
    };
}
