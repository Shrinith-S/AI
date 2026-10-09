using Chitra.Domain.Enums;

namespace Chitra.Application.Abstractions.Ai;

/// <summary>
/// A model that can be selected in the UI. Definitions are loaded from <c>ai/models.json</c>, so models and their
/// workflows can be added without code changes.
/// </summary>
public sealed class AiModelDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string Provider { get; init; } = "ComfyUI";
    public string? Description { get; init; }
    public string? License { get; init; }
    public string? Homepage { get; init; }

    public List<GenerationType> Capabilities { get; init; } = [];

    /// <summary>Workflow template file per capability name, e.g. { "TextToImage": "sdxl_t2i.json" }.</summary>
    public Dictionary<string, string> Workflows { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ModelFileDefinition> Files { get; init; } = [];

    public double MinVramGb { get; init; }
    public double RecommendedVramGb { get; init; }
    public ModelDefaults Defaults { get; init; } = new();

    /// <summary>ComfyUI custom node packs the workflows depend on (empty when only core nodes are used).</summary>
    public List<string> RequiredCustomNodes { get; init; } = [];
    public string? Notes { get; init; }

    public long TotalSizeBytes => Files.Sum(f => f.SizeBytes);

    public bool Supports(GenerationType type) => Capabilities.Contains(type);

    public string? GetWorkflow(GenerationType type) => Workflows.GetValueOrDefault(type.ToString());
}

public sealed class ModelFileDefinition
{
    /// <summary>Placeholder role used by workflow templates, e.g. "checkpoint" maps to {{file.checkpoint}}.</summary>
    public string Role { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;

    /// <summary>ComfyUI models sub-folder: checkpoints, diffusion_models, text_encoders, vae, clip_vision, ...</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>Approximate size; the exact size is read from the server before any download.</summary>
    public long SizeBytes { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed class ModelDefaults
{
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 1024;
    public int Steps { get; init; } = 25;
    public double Cfg { get; init; } = 7;
    public string Sampler { get; init; } = "euler";
    public string Scheduler { get; init; } = "normal";
    public string? NegativePrompt { get; init; }

    /// <summary>Frame rate the model was trained at.</summary>
    public int NativeFps { get; init; } = 24;
    public int MaxFrames { get; init; } = 121;

    /// <summary>Frame counts must be of the form <c>FrameMultiple * n + 1</c> (8 for LTX-Video, 4 for Wan/Hunyuan).</summary>
    public int FrameMultiple { get; init; } = 8;

    /// <summary>Width and height must be divisible by this value.</summary>
    public int DimensionMultiple { get; init; } = 8;
    public int MaxDimension { get; init; } = 2048;

    /// <summary>Optional model-sampling shift used by flow-matching video models.</summary>
    public double? Shift { get; init; }
}

public interface IModelCatalog
{
    IReadOnlyList<AiModelDefinition> All { get; }
    AiModelDefinition? Find(string id);
}
