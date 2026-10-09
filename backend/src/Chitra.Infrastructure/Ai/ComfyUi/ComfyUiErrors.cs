using System.Text.Json.Nodes;
using Chitra.Application.Common;

namespace Chitra.Infrastructure.Ai.ComfyUi;

/// <summary>Translates ComfyUI's error payloads into actionable messages.</summary>
public static class ComfyUiErrors
{
    private static readonly string[] ModelInputs = ["ckpt_name", "unet_name", "clip_name", "clip_name1", "clip_name2", "vae_name", "lora_name"];

    /// <summary>Errors returned by POST /prompt when the workflow fails validation.</summary>
    public static AppException FromPromptRejection(JsonNode? json, int statusCode)
    {
        var error = json?["error"];
        var type = error?["type"]?.GetValue<string>() ?? string.Empty;
        var message = error?["message"]?.GetValue<string>() ?? $"HTTP {statusCode}";

        if (type is "missing_node_type" || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return new AppException(ErrorCode.InvalidWorkflow,
                $"ComfyUI cannot run this workflow: {message} Update ComfyUI to a recent version or install the custom nodes the model needs.");
        }

        var details = new List<string>();
        if (json?["node_errors"] is JsonObject nodeErrors)
        {
            foreach (var (nodeId, nodeError) in nodeErrors)
            {
                var classType = nodeError?["class_type"]?.GetValue<string>() ?? "node";
                foreach (var e in nodeError?["errors"]?.AsArray() ?? [])
                {
                    var errorType = e?["type"]?.GetValue<string>();
                    var detail = e?["details"]?.GetValue<string>() ?? e?["message"]?.GetValue<string>() ?? "invalid input";
                    if (errorType == "value_not_in_list" && ModelInputs.Any(m => detail.StartsWith(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        var file = ExtractQuoted(detail);
                        return new AppException(ErrorCode.ModelMissing,
                            $"Model file {(file is null ? string.Empty : $"'{file}' ")}is not installed in ComfyUI ({classType}). " +
                            "Download it from the Models page or copy it into the matching ComfyUI models folder, then restart/refresh ComfyUI.");
                    }

                    details.Add($"{classType} #{nodeId}: {detail}");
                }
            }
        }

        var suffix = details.Count > 0 ? " " + string.Join("; ", details.Take(5)) : string.Empty;
        return new AppException(ErrorCode.InvalidWorkflow, $"ComfyUI rejected the workflow: {message}.{suffix}");
    }

    /// <summary>Errors recorded in /history after a prompt failed during execution.</summary>
    public static AppException FromExecutionError(JsonObject? errorData)
    {
        var exceptionType = errorData?["exception_type"]?.GetValue<string>() ?? string.Empty;
        var exceptionMessage = errorData?["exception_message"]?.GetValue<string>()?.Trim() ?? "Unknown error";
        var nodeType = errorData?["node_type"]?.GetValue<string>() ?? "a node";

        if (IsOutOfMemory(exceptionType, exceptionMessage))
        {
            return new AppException(ErrorCode.InsufficientVram,
                $"The GPU ran out of memory (VRAM) in {nodeType}. Lower the resolution, duration/frame count or batch size, choose a smaller model, " +
                "or start ComfyUI with --lowvram.");
        }

        if (exceptionMessage.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
            exceptionMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) && nodeType.Contains("Loader", StringComparison.OrdinalIgnoreCase))
        {
            return new AppException(ErrorCode.ModelMissing, $"{nodeType} could not load a model file: {exceptionMessage}");
        }

        return new AppException(ErrorCode.GenerationFailed, $"ComfyUI failed in {nodeType}: {Truncate(exceptionMessage, 600)}");
    }

    public static bool IsOutOfMemory(string exceptionType, string message) =>
        exceptionType.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("out of memory", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Allocation on device", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractQuoted(string text)
    {
        var start = text.IndexOf('\'');
        var end = start >= 0 ? text.IndexOf('\'', start + 1) : -1;
        return end > start ? text[(start + 1)..end] : null;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
