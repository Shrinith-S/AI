using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Abstractions.Tts;
using Chitra.Application.Dtos;
using Chitra.Application.Generations;
using Chitra.Application.Models;
using Chitra.Application.Settings;

namespace Chitra.Application.SystemStatus;

public sealed class SystemStatusService(
    IRuntimeSettings settings,
    IAiProviderRegistry providers,
    IFfmpegService ffmpeg,
    ITtsProviderRegistry tts,
    IMediaStorage storage,
    ISystemInfoService systemInfo,
    ModelService models,
    GenerationService generations)
{
    public const string CostNotice =
        "Local AI Mode — No API charges. Your actual cost depends on your hardware and electricity: " +
        "GPU inference uses power, and cloud GPUs (if you point ComfyUI at one) are billed by their provider.";

    public async Task<SystemStatusDto> GetAsync(CancellationToken ct)
    {
        var current = settings.Current;

        var providerTask = GetProviderStatusAsync(current, ct);
        var ffmpegTask = ffmpeg.GetStatusAsync(ct);
        var ttsTask = GetTtsStatusAsync(ct);
        var systemTask = systemInfo.GetAsync(ct);
        var hardwareTask = models.GetHardwareAsync(ct);
        await Task.WhenAll(providerTask, ffmpegTask, ttsTask, systemTask, hardwareTask);

        var ff = ffmpegTask.Result;
        var system = systemTask.Result;
        var (rating, reason) = ModelService.RateHardware(hardwareTask.Result);
        var (queued, processing) = await generations.GetQueueCountsAsync(ct);

        return new SystemStatusDto(
            current.LocalOnly,
            CostNotice,
            providerTask.Result,
            new ComponentStatusDto("FFmpeg", ff.IsAvailable, ff.Message, ff.Version),
            ttsTask.Result,
            GetStorageStatus(system),
            system,
            rating,
            reason,
            queued,
            processing);
    }

    private async Task<ComponentStatusDto> GetProviderStatusAsync(StudioSettings current, CancellationToken ct)
    {
        try
        {
            var provider = providers.Get(current.AiProvider);
            var health = await provider.CheckHealthAsync(ct);
            return new ComponentStatusDto(provider.Name, health.IsAvailable, health.Message, health.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ComponentStatusDto(current.AiProvider, false, ex.Message);
        }
    }

    private async Task<ComponentStatusDto> GetTtsStatusAsync(CancellationToken ct)
    {
        try
        {
            var provider = tts.GetActive();
            var health = await provider.CheckHealthAsync(ct);
            return new ComponentStatusDto(provider.Name, health.IsAvailable, health.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ComponentStatusDto(settings.Current.TtsProvider, false, ex.Message);
        }
    }

    private ComponentStatusDto GetStorageStatus(SystemInfoDto system)
    {
        try
        {
            storage.EnsureFreeSpace();
            var free = system.Disk is null ? "unknown" : $"{system.Disk.FreeBytes / 1024d / 1024 / 1024:0.#} GB free";
            return new ComponentStatusDto("Storage", true, $"{storage.RootPath} ({free})");
        }
        catch (Exception ex)
        {
            return new ComponentStatusDto("Storage", false, ex.Message);
        }
    }
}
