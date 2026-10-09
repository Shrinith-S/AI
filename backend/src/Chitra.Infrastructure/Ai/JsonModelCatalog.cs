using System.Text.Json;
using System.Text.Json.Serialization;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Application.Settings;
using Chitra.Infrastructure.Ai.ComfyUi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chitra.Infrastructure.Ai;

/// <summary>Loads model definitions from ai/models.json and checks that each referenced workflow exists.</summary>
public sealed class JsonModelCatalog : IModelCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, AiModelDefinition> _byId;

    public JsonModelCatalog(IOptions<AiOptions> options, ILogger<JsonModelCatalog> logger)
    {
        var path = Path.IsPathRooted(options.Value.ModelCatalogPath)
            ? options.Value.ModelCatalogPath
            : Path.Combine(AppContext.BaseDirectory, options.Value.ModelCatalogPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Model catalog not found at {path}.", path);
        }

        var document = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(path), Json)
                       ?? throw new InvalidOperationException($"Model catalog {path} is empty.");
        All = document.Models;
        _byId = document.Models.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

        var workflowDir = FileWorkflowTemplateStore.ResolveDirectory(options.Value.WorkflowsPath);
        foreach (var model in All)
        {
            foreach (var workflow in model.Workflows.Values.Where(w => !File.Exists(Path.Combine(workflowDir, w))))
            {
                logger.LogWarning("Model {Model} references missing workflow {Workflow}", model.Id, workflow);
            }
        }

        logger.LogInformation("Loaded {Count} model definitions from {Path}", All.Count, path);
    }

    public IReadOnlyList<AiModelDefinition> All { get; }

    public AiModelDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    private sealed class CatalogDocument
    {
        public List<AiModelDefinition> Models { get; init; } = [];
    }
}

public sealed class AiProviderRegistry(IEnumerable<IAiGenerationProvider> providers, IRuntimeSettings settings) : IAiProviderRegistry
{
    public IReadOnlyList<IAiGenerationProvider> All { get; } = providers.ToList();

    public IAiGenerationProvider Get(string name)
    {
        var provider = All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new AppException(ErrorCode.ProviderUnavailable,
                           $"AI provider '{name}' is not available. Installed providers: {string.Join(", ", All.Select(p => p.Name))}.");

        var current = settings.Current;
        if (current.LocalOnly && (provider.IncursApiCharges || !provider.IsLocal))
        {
            throw new AppException(ErrorCode.LocalOnlyViolation, provider.IncursApiCharges
                ? $"{provider.Name} is a paid API and Local-Only mode is on. Turn off Local-Only in Settings to use it."
                : $"{provider.Name} runs on remote servers: your prompts and input images are uploaded there. It costs nothing within its free quota, " +
                  "but Local-Only mode keeps everything on this machine. Turn off Local-Only in Settings to use it.");
        }

        if (current.LocalOnly && provider is ComfyUiProvider && !LocalNetwork.IsLocalUrl(current.ComfyUiUrl))
        {
            throw new AppException(ErrorCode.LocalOnlyViolation,
                $"Local-Only mode is on but ComfyUI is configured at {current.ComfyUiUrl}, which is not on this machine or a private network. " +
                "Remote GPU servers usually cost money; turn off Local-Only in Settings if this is intentional.");
        }

        return provider;
    }

    public IAiGenerationProvider GetForModel(AiModelDefinition model)
    {
        // Kaggle executes the same ComfyUI workflows remotely, so it can stand in for the ComfyUI engine.
        if (model.Provider.Equals(ComfyUiProvider.ProviderName, StringComparison.OrdinalIgnoreCase) &&
            settings.Current.AiProvider.Equals(Kaggle.KaggleProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return Get(Kaggle.KaggleProvider.ProviderName);
        }

        return Get(model.Provider);
    }
}

public static class LocalNetwork
{
    /// <summary>True for loopback, private (RFC 1918 / ULA / link-local) addresses and single-label or .local/.internal hosts.</summary>
    public static bool IsLocalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.IdnHost;
        if (uri.IsLoopback || !host.Contains('.') ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }
}
