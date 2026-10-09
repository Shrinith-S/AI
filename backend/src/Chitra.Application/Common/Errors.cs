namespace Chitra.Application.Common;

/// <summary>Machine-readable error codes surfaced to the UI alongside a human-readable message.</summary>
public enum ErrorCode
{
    Validation,
    NotFound,
    ProviderUnavailable,
    ModelMissing,
    InsufficientVram,
    GenerationFailed,
    InvalidWorkflow,
    FfmpegUnavailable,
    TtsUnavailable,
    DiskFull,
    Timeout,
    Cancelled,
    LocalOnlyViolation,
    Conflict,
}

/// <summary>An expected failure with a message that is safe and useful to show to the user.</summary>
public class AppException(ErrorCode code, string message, Exception? inner = null) : Exception(message, inner)
{
    public ErrorCode Code { get; } = code;
}

public sealed class NotFoundException(string entity, object id)
    : AppException(ErrorCode.NotFound, $"{entity} '{id}' was not found.");

public sealed class ValidationException : AppException
{
    public ValidationException(IDictionary<string, string[]> errors)
        : base(ErrorCode.Validation, "One or more fields are invalid: " + string.Join(" ", errors.SelectMany(e => e.Value)))
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Collects field errors and throws a single <see cref="ValidationException"/>.</summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public Validator Require(bool condition, string field, string message)
    {
        if (!condition)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }

        return this;
    }

    public Validator NotEmpty(string? value, string field, int maxLength = 4000) =>
        Require(!string.IsNullOrWhiteSpace(value), field, $"{field} is required.")
            .Require(value is null || value.Length <= maxLength, field, $"{field} must be at most {maxLength} characters.");

    public Validator Range(double? value, double min, double max, string field) =>
        Require(value is null || (value >= min && value <= max), field, $"{field} must be between {min} and {max}.");

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
