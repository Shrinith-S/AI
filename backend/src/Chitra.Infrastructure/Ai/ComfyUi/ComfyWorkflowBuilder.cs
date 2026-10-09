using System.Text.Json.Nodes;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Domain.Enums;

namespace Chitra.Infrastructure.Ai.ComfyUi;

/// <summary>
/// Turns a generation request into a ready-to-run ComfyUI API workflow. Shared by every provider that executes
/// ComfyUI workflows (local ComfyUI, the Kaggle batch worker), so prompts and workflows are built identically.
/// </summary>
public static class ComfyWorkflowBuilder
{
    /// <param name="inventory">Installed files reported by ComfyUI, used to resolve sub-folder paths; null uses catalog names.</param>
    /// <param name="inputImageName">Name of the input image inside ComfyUI's input folder (image-to-video only).</param>
    public static JsonObject Build(
        IWorkflowTemplateStore workflows,
        ProviderGenerationRequest request,
        GenerationType type,
        ModelInventory? inventory,
        string? inputImageName = null)
    {
        var model = request.Model;
        var workflowFile = model.GetWorkflow(type)
                           ?? throw new AppException(ErrorCode.InvalidWorkflow, $"{model.Name} has no ComfyUI workflow configured for {type}.");

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = request.Prompt,
            ["negative_prompt"] = request.NegativePrompt,
            ["seed"] = request.Seed,
            ["steps"] = request.Steps,
            ["cfg"] = request.CfgScale,
            ["width"] = request.Width,
            ["height"] = request.Height,
            ["sampler"] = request.Sampler ?? model.Defaults.Sampler,
            ["scheduler"] = request.Scheduler ?? model.Defaults.Scheduler,
            ["denoise"] = 1.0,
            ["shift"] = model.Defaults.Shift ?? 8.0,
            ["filename_prefix"] = $"chitra/{request.GenerationId:N}",
        };

        if (request is ImageGenerationRequest image)
        {
            parameters["batch_size"] = image.BatchSize;
        }

        if (request is VideoGenerationRequest video)
        {
            parameters["frames"] = video.FrameCount;
            parameters["fps"] = video.Fps;
            parameters["motion_strength"] = video.MotionStrength;
            // LTX-Video conditions on the frame rate; Wan models ignore it.
            parameters["frame_rate"] = (double)video.Fps;
        }

        if (inputImageName is not null)
        {
            parameters["image"] = inputImageName;
        }

        foreach (var file in model.Files)
        {
            // Use the name ComfyUI knows the file by (it may live in a sub-folder).
            parameters[$"file.{file.Role}"] = inventory?.Find(file.Folder, file.FileName) ?? file.FileName;
        }

        return WorkflowRenderer.Render(workflows.Load(workflowFile), parameters);
    }
}
