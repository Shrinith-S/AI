using System.Text.Json.Nodes;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Ai.ComfyUi;

/// <summary>
/// Runs generations on a ComfyUI server: renders the model's workflow template, submits it to /prompt,
/// tracks progress over the WebSocket, confirms completion through /history and downloads outputs via /view.
/// </summary>
public sealed class ComfyUiProvider(
    ComfyUiClient client,
    ComfyUiProgressTracker tracker,
    IWorkflowTemplateStore workflows,
    ILogger<ComfyUiProvider> logger) : IAiGenerationProvider
{
    public const string ProviderName = "ComfyUI";

    /// <summary>ComfyUI loader nodes that list installed files, per models sub-folder.</summary>
    private static readonly (string Folder, string Node, string Input)[] InventorySources =
    [
        ("checkpoints", "CheckpointLoaderSimple", "ckpt_name"),
        ("diffusion_models", "UNETLoader", "unet_name"),
        ("text_encoders", "CLIPLoader", "clip_name"),
        ("vae", "VAELoader", "vae_name"),
        ("clip_vision", "CLIPVisionLoader", "clip_name"),
        ("loras", "LoraLoader", "lora_name"),
    ];

    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static (DateTime At, string Url, ProviderHealth Health)? _healthCache;
    private static (DateTime At, string Url, ModelInventory Inventory)? _inventoryCache;

    public string Name => ProviderName;

    /// <summary>ComfyUI runs on the user's own machine or server; no per-request charges.</summary>
    public bool IsLocal => true;
    public bool IncursApiCharges => false;

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var url = client.BaseUri.ToString();
        if (_healthCache is { } cached && cached.Url == url && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(3))
        {
            return cached.Health;
        }

        ProviderHealth health;
        try
        {
            // Windows retries refused localhost connections for ~2s per address (IPv6 then IPv4), so allow 6s.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            var stats = await client.GetSystemStatsAsync(cts.Token);
            var version = stats["system"]?["comfyui_version"]?.GetValue<string>();
            var devices = (stats["devices"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(d => new ProviderDevice(
                    d["name"]?.GetValue<string>() ?? "unknown",
                    d["type"]?.GetValue<string>() ?? "unknown",
                    d["vram_total"]?.GetValue<long>() ?? 0,
                    d["vram_free"]?.GetValue<long>() ?? 0))
                .ToList();
            var gpu = devices.FirstOrDefault(d => d.Type != "cpu");
            var device = gpu is null ? "CPU only" : $"{gpu.Name} ({gpu.VramTotalBytes / 1024d / 1024 / 1024:0.#} GB)";
            health = new ProviderHealth(true, $"ComfyUI {version} is running at {url.TrimEnd('/')} on {device}.", version, devices);
        }
        catch (AppException ex)
        {
            health = new ProviderHealth(false, ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            health = new ProviderHealth(false, $"{client.NotRunningMessage} (No response within 6 seconds.)");
        }
        catch (HttpRequestException ex)
        {
            health = new ProviderHealth(false, $"ComfyUI at {url.TrimEnd('/')} returned an error: {ex.Message}");
        }

        _healthCache = (DateTime.UtcNow, url, health);
        return health;
    }

    public Task<ProviderSubmission> GenerateImageAsync(ImageGenerationRequest request, CancellationToken cancellationToken) =>
        SubmitAsync(request, GenerationType.TextToImage, null, cancellationToken);

    public Task<ProviderSubmission> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken cancellationToken) =>
        SubmitAsync(request, GenerationType.TextToVideo, null, cancellationToken);

    public async Task<ProviderSubmission> GenerateImageToVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken)
    {
        var uploadedName = await client.UploadImageAsync(request.InputImagePath, cancellationToken);
        return await SubmitAsync(request, GenerationType.ImageToVideo, uploadedName, cancellationToken);
    }

    public async Task<ProviderJobStatus> GetGenerationStatusAsync(string providerJobId, CancellationToken cancellationToken)
    {
        var progress = tracker.Get(providerJobId);
        if (progress is not null && tracker.IsConnected)
        {
            lock (progress)
            {
                if (progress.Started && !progress.Finished)
                {
                    return new ProviderJobStatus(ProviderJobState.Running, progress.Percent, progress.Message, []);
                }
            }
        }

        var history = await client.GetHistoryAsync(providerJobId, cancellationToken);
        if (history is not null)
        {
            var status = ParseHistory(history);
            if (status.State is ProviderJobState.Completed or ProviderJobState.Failed or ProviderJobState.Cancelled)
            {
                tracker.Forget(providerJobId);
            }

            return status;
        }

        var (running, pending) = await client.GetQueueAsync(cancellationToken);
        if (running.Contains(providerJobId))
        {
            return new ProviderJobStatus(ProviderJobState.Running, progress?.Percent ?? 0, progress?.Message ?? "Generating", []);
        }

        var position = pending.ToList().IndexOf(providerJobId);
        if (position >= 0)
        {
            var ahead = position + running.Count;
            return new ProviderJobStatus(ProviderJobState.Pending, 0,
                ahead == 0 ? "Starting in ComfyUI" : $"Waiting in ComfyUI queue ({ahead} ahead)", []);
        }

        return new ProviderJobStatus(ProviderJobState.Unknown, 0, "Waiting for ComfyUI", []);
    }

    public Task<Stream> OpenOutputAsync(ProviderOutputFile output, CancellationToken cancellationToken) =>
        client.ViewAsync(output.Locator, cancellationToken);

    public async Task CancelAsync(string providerJobId, CancellationToken cancellationToken)
    {
        var (running, pending) = await client.GetQueueAsync(cancellationToken);
        if (pending.Contains(providerJobId))
        {
            await client.DeleteFromQueueAsync(providerJobId, cancellationToken);
        }
        else if (running.Contains(providerJobId))
        {
            await client.InterruptAsync(providerJobId, cancellationToken);
        }

        tracker.Forget(providerJobId);
    }

    public async Task<ModelInventory?> GetModelInventoryAsync(CancellationToken cancellationToken)
    {
        var url = client.BaseUri.ToString();
        if (_inventoryCache is { } cached && cached.Url == url && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(15))
        {
            return cached.Inventory;
        }

        if (!(await CheckHealthAsync(cancellationToken)).IsAvailable) return null;

        await CacheLock.WaitAsync(cancellationToken);
        try
        {
            var files = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (folder, node, input) in InventorySources)
            {
                var info = await client.GetObjectInfoAsync(node, cancellationToken);
                files[folder] = ExtractOptions(info?["input"]?["required"]?[input]);
            }

            var inventory = new ModelInventory(files);
            _inventoryCache = (DateTime.UtcNow, url, inventory);
            return inventory;
        }
        catch (AppException ex)
        {
            logger.LogDebug(ex, "Could not read ComfyUI model inventory");
            return null;
        }
        finally
        {
            CacheLock.Release();
        }
    }

    /// <summary>Drops cached health/inventory, e.g. after a model download completed.</summary>
    public static void InvalidateCaches()
    {
        _healthCache = null;
        _inventoryCache = null;
    }

    private async Task<ProviderSubmission> SubmitAsync(
        ProviderGenerationRequest request,
        GenerationType type,
        string? inputImageName,
        CancellationToken ct)
    {
        var inventory = await GetModelInventoryAsync(ct);
        var workflow = ComfyWorkflowBuilder.Build(workflows, request, type, inventory, inputImageName);
        var promptId = await client.QueuePromptAsync(workflow, tracker.ClientId, ct);
        tracker.Register(promptId, WorkflowRenderer.NodeTypes(workflow));
        logger.LogInformation("Submitted {Model}/{Type} to ComfyUI as prompt {PromptId}", request.Model.Id, type, promptId);

        var warnings = new List<string>();
        if (!tracker.IsConnected)
        {
            warnings.Add("Live step progress is unavailable (ComfyUI WebSocket not connected); completion is still detected by polling.");
        }

        return new ProviderSubmission(promptId, warnings);
    }

    internal static ProviderJobStatus ParseHistory(JsonObject history)
    {
        var status = history["status"] as JsonObject;
        var statusString = status?["status_str"]?.GetValue<string>();
        var messages = status?["messages"] as JsonArray ?? [];

        JsonObject? FindMessage(string name) => messages
            .OfType<JsonArray>()
            .Where(m => m.Count > 1 && m[0]?.GetValue<string>() == name)
            .Select(m => m[1] as JsonObject)
            .FirstOrDefault();

        if (statusString == "error")
        {
            if (FindMessage("execution_interrupted") is not null)
            {
                return new ProviderJobStatus(ProviderJobState.Cancelled, 0, "Interrupted", []);
            }

            var error = ComfyUiErrors.FromExecutionError(FindMessage("execution_error"));
            return new ProviderJobStatus(ProviderJobState.Failed, 0, "Failed", [], error.Code, error.Message);
        }

        var completed = status?["completed"]?.GetValue<bool>() ?? statusString == "success";
        if (!completed)
        {
            return new ProviderJobStatus(ProviderJobState.Running, 95, "Finishing", []);
        }

        return new ProviderJobStatus(ProviderJobState.Completed, 100, "Completed", ParseOutputs(history["outputs"] as JsonObject));
    }

    internal static IReadOnlyList<ProviderOutputFile> ParseOutputs(JsonObject? outputs)
    {
        var files = new List<ProviderOutputFile>();
        if (outputs is null) return files;

        foreach (var (_, nodeOutput) in outputs)
        {
            if (nodeOutput is not JsonObject node) continue;
            var animated = node["animated"] is JsonArray flags && flags.Any(f => f?.GetValue<bool>() == true);

            foreach (var (_, value) in node)
            {
                if (value is not JsonArray items) continue;
                foreach (var item in items.OfType<JsonObject>())
                {
                    var fileName = item["filename"]?.GetValue<string>();
                    var folderType = item["type"]?.GetValue<string>() ?? "output";
                    if (fileName is null || folderType == "temp") continue;

                    var subfolder = item["subfolder"]?.GetValue<string>() ?? string.Empty;
                    var mediaType = Classify(fileName, animated);
                    if (mediaType is null) continue;

                    var locator = $"filename={Uri.EscapeDataString(fileName)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(folderType)}";
                    if (files.All(f => f.Locator != locator))
                    {
                        files.Add(new ProviderOutputFile(fileName, mediaType.Value, locator));
                    }
                }
            }
        }

        return files;
    }

    private static MediaType? Classify(string fileName, bool animated) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mp4" or ".webm" or ".mov" or ".mkv" => MediaType.Video,
        ".gif" => animated ? MediaType.Video : MediaType.Image,
        ".webp" => animated ? MediaType.Video : MediaType.Image,
        ".png" or ".jpg" or ".jpeg" => MediaType.Image,
        ".wav" or ".mp3" or ".flac" or ".ogg" => MediaType.Audio,
        _ => null,
    };

    private static List<string> ExtractOptions(JsonNode? inputSpec)
    {
        if (inputSpec is not JsonArray spec || spec.Count == 0) return [];

        // Legacy format: [["a.safetensors", ...], {...}]; newer: ["COMBO", {"options": [...]}].
        var list = spec[0] as JsonArray ?? (spec.Count > 1 ? spec[1]?["options"] as JsonArray : null);
        return list?.Select(n => n?.GetValue<string>()).OfType<string>().ToList() ?? [];
    }
}
