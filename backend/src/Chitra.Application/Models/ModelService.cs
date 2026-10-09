using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Chitra.Application.Models;

/// <summary>Best GPU memory available to the inference engine, as reported by ComfyUI or nvidia-smi.</summary>
public sealed record HardwareProfile(double? VramGb, string? GpuName, string Source);

public sealed class ModelService(
    IModelCatalog catalog,
    IAiProviderRegistry providers,
    ISystemInfoService systemInfo,
    IModelDownloadService downloads,
    Settings.IRuntimeSettings settings,
    ILogger<ModelService> logger)
{
    public async Task<IReadOnlyList<ModelDto>> ListAsync(GenerationType? capability, CancellationToken ct)
    {
        var hardware = await GetHardwareAsync(ct);
        var inventories = new Dictionary<string, (ModelInventory? Inventory, bool OnDemand)>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ModelDto>();

        foreach (var model in catalog.All.Where(m => capability is null || m.Supports(capability.Value)))
        {
            if (!inventories.TryGetValue(model.Provider, out var entry))
            {
                entry = await TryGetInventoryAsync(model, ct);
                inventories[model.Provider] = entry;
            }

            result.Add(ToDto(model, entry.Inventory, hardware, downloads.GetStatus(model.Id), entry.OnDemand));
        }

        return result;
    }

    public async Task<ModelDto> GetAsync(string id, CancellationToken ct)
    {
        var model = catalog.Find(id) ?? throw new NotFoundException("Model", id);
        var (inventory, onDemand) = await TryGetInventoryAsync(model, ct);
        return ToDto(model, inventory, await GetHardwareAsync(ct), downloads.GetStatus(model.Id), onDemand);
    }

    public async Task<HardwareProfile> GetHardwareAsync(CancellationToken ct)
    {
        // Prefer what the active inference engine sees: it may be another machine, a container or a Kaggle GPU.
        try
        {
            var provider = providers.Get(settings.Current.AiProvider);
            var health = await provider.CheckHealthAsync(ct);
            var gpu = health.Devices?.Where(d => d.Type is "cuda" or "rocm" or "mps" or "xpu" && d.VramTotalBytes > 0)
                .OrderByDescending(d => d.VramTotalBytes).FirstOrDefault();
            if (gpu is not null)
            {
                return new HardwareProfile(gpu.VramTotalBytes / 1024d / 1024 / 1024, gpu.Name, provider.Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Active provider health check failed");
        }

        var info = await systemInfo.GetAsync(ct);
        var best = info.Gpus.Where(g => g.VramTotalMb > 0).OrderByDescending(g => g.VramTotalMb).FirstOrDefault();
        return best is null
            ? new HardwareProfile(null, null, info.NvidiaSmiAvailable ? "nvidia-smi" : "none")
            : new HardwareProfile(best.VramTotalMb / 1024d, best.Name, best.Source);
    }

    public static (string Rating, string Reason) Rate(AiModelDefinition model, HardwareProfile hardware)
    {
        if (hardware.VramGb is not { } vram)
        {
            var smallImageModel = model.MinVramGb <= 4 && model.Capabilities.All(c => c == GenerationType.TextToImage);
            return smallImageModel
                ? ("Limited", "No supported GPU detected. This small image model can run on CPU in ComfyUI, but each image may take minutes.")
                : ("Insufficient", $"No supported GPU detected. {model.Name} needs at least {model.MinVramGb:0.#} GB of GPU memory to be practical.");
        }

        if (vram >= model.RecommendedVramGb)
        {
            return ("Recommended", $"{hardware.GpuName ?? "Your GPU"} has {vram:0.#} GB VRAM (recommended: {model.RecommendedVramGb:0.#} GB).");
        }

        if (vram >= model.MinVramGb)
        {
            return ("Limited", $"{vram:0.#} GB VRAM meets the {model.MinVramGb:0.#} GB minimum. Expect slower generation; use lower resolution or fewer frames.");
        }

        return ("Insufficient", $"{vram:0.#} GB VRAM is below the {model.MinVramGb:0.#} GB minimum for {model.Name}.");
    }

    public static (string Rating, string Reason) RateHardware(HardwareProfile hardware) => hardware.VramGb switch
    {
        null => ("Insufficient", "No NVIDIA GPU detected. Image generation on CPU is possible but very slow; video generation is not practical."),
        >= 16 => ("Recommended", $"{hardware.VramGb:0.#} GB VRAM handles SDXL, FLUX and the included video models."),
        >= 8 => ("Limited", $"{hardware.VramGb:0.#} GB VRAM handles SD 1.5/SDXL and small video models (LTX-Video, Wan 1.3B) at modest resolutions."),
        _ => ("Insufficient", $"{hardware.VramGb:0.#} GB VRAM is enough only for SD 1.5 at low resolution."),
    };

    internal ModelDto ToDto(AiModelDefinition model, ModelInventory? inventory, HardwareProfile hardware, ModelDownloadStatusDto? download,
        bool downloadsOnDemand = false)
    {
        var files = model.Files.Select(f =>
        {
            var installedAs = inventory?.Find(f.Folder, f.FileName);
            return new ModelFileStatusDto(f.Role, f.FileName, f.Folder, f.SizeBytes, f.DownloadUrl,
                inventory is null ? null : installedAs is not null, installedAs);
        }).ToList();

        var installState = downloadsOnDemand
            ? model.Files.All(f => f.DownloadUrl is not null) ? "OnDemand" : "NotInstalled"
            : inventory is null
                ? "Unknown"
                : files.All(f => f.Installed == true) ? "Installed" : files.Any(f => f.Installed == true) ? "Partial" : "NotInstalled";

        var (rating, reason) = Rate(model, hardware);
        var d = model.Defaults;
        return new ModelDto(model.Id, model.Name, model.Family, model.Provider, model.Description, model.Capabilities,
            model.TotalSizeBytes, model.MinVramGb, model.RecommendedVramGb, installState, files, rating, reason,
            model.RequiredCustomNodes, model.Notes, model.License, model.Homepage,
            new ModelDefaultsDto(d.Width, d.Height, d.Steps, d.Cfg, d.NativeFps, d.MaxFrames), download);
    }

    internal async Task<(ModelInventory? Inventory, bool OnDemand)> TryGetInventoryAsync(AiModelDefinition model, CancellationToken ct)
    {
        try
        {
            var provider = providers.GetForModel(model);
            return provider.DownloadsModelsOnDemand ? (null, true) : (await provider.GetModelInventoryAsync(ct), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read model inventory for {Model}", model.Id);
            return (null, false);
        }
    }
}
