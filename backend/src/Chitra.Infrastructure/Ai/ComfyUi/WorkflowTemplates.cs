using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Chitra.Application.Common;
using Microsoft.Extensions.Options;

namespace Chitra.Infrastructure.Ai.ComfyUi;

/// <summary>
/// Loads ComfyUI workflows exported in "API format". Templates contain placeholders such as <c>{{prompt}}</c> or
/// <c>{{file.checkpoint}}</c>, so new workflows can be dropped in without touching code.
/// </summary>
public interface IWorkflowTemplateStore
{
    JsonObject Load(string workflowFile);
}

public sealed class FileWorkflowTemplateStore(IOptions<AiOptions> options) : IWorkflowTemplateStore
{
    private readonly string _directory = ResolveDirectory(options.Value.WorkflowsPath);

    public JsonObject Load(string workflowFile)
    {
        var fileName = Path.GetFileName(workflowFile);
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            throw new AppException(ErrorCode.InvalidWorkflow, $"Workflow template '{fileName}' was not found in {_directory}.");
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new AppException(ErrorCode.InvalidWorkflow, $"Workflow template '{fileName}' is not a JSON object.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new AppException(ErrorCode.InvalidWorkflow, $"Workflow template '{fileName}' is not valid JSON: {ex.Message}", ex);
        }
    }

    internal static string ResolveDirectory(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}

public static partial class WorkflowRenderer
{
    /// <summary>
    /// Replaces placeholders. A string that is exactly "{{name}}" becomes the typed value (number, bool, string);
    /// placeholders inside longer strings are substituted as text. Top-level keys starting with "_" are comments
    /// and are removed. Unknown placeholders are an error.
    /// </summary>
    public static JsonObject Render(JsonObject template, IReadOnlyDictionary<string, object?> parameters)
    {
        var result = (JsonObject)template.DeepClone();
        foreach (var key in result.Select(p => p.Key).Where(k => k.StartsWith('_')).ToList())
        {
            result.Remove(key);
        }

        foreach (var (key, node) in result.ToList())
        {
            result[key] = Replace(node, parameters);
        }

        return result;
    }

    /// <summary>Node id to class_type, used for progress messages.</summary>
    public static IReadOnlyDictionary<string, string> NodeTypes(JsonObject workflow) =>
        workflow.Where(p => p.Value is JsonObject)
            .ToDictionary(p => p.Key, p => p.Value!["class_type"]?.GetValue<string>() ?? "node");

    private static JsonNode? Replace(JsonNode? node, IReadOnlyDictionary<string, object?> parameters)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    obj[key] = Replace(child, parameters);
                }

                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var replaced = Replace(array[i], parameters);
                    if (!ReferenceEquals(replaced, array[i])) array[i] = replaced;
                }

                return array;

            case JsonValue value when value.TryGetValue<string>(out var text) && text.Contains("{{"):
                var exact = ExactPlaceholder().Match(text);
                if (exact.Success)
                {
                    return ToNode(Lookup(exact.Groups[1].Value, parameters));
                }

                return JsonValue.Create(InlinePlaceholder().Replace(text, m => Format(Lookup(m.Groups[1].Value, parameters))));

            default:
                return node;
        }
    }

    private static object? Lookup(string name, IReadOnlyDictionary<string, object?> parameters) =>
        parameters.TryGetValue(name, out var value)
            ? value
            : throw new AppException(ErrorCode.InvalidWorkflow, $"Workflow uses the placeholder '{{{{{name}}}}}' but no value is provided for it.");

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        _ => JsonValue.Create(value.ToString()),
    };

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    [GeneratedRegex(@"^\{\{\s*([\w.]+)\s*\}\}$")]
    private static partial Regex ExactPlaceholder();

    [GeneratedRegex(@"\{\{\s*([\w.]+)\s*\}\}")]
    private static partial Regex InlinePlaceholder();
}
