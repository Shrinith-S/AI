using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Tts;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Media;
using Chitra.Domain.Enums;

namespace Chitra.Application.Tts;

/// <summary>Local narration via the configured TTS engine (Piper by default).</summary>
public sealed class TtsService(ITtsProviderRegistry registry, IMediaStorage storage, MediaService media, ICurrentUser currentUser, IAppDbContext db)
{
    public async Task<TtsStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var provider = registry.GetActive();
        var health = await provider.CheckHealthAsync(ct);
        var voices = await provider.GetVoicesAsync(ct);
        return new TtsStatusDto(provider.Name, health.IsAvailable, health.Message,
            voices.Select(v => new TtsVoiceDto(v.Id, v.Name, v.Language)).ToList());
    }

    public async Task<MediaAssetDto> SynthesizeAsync(SynthesizeSpeechRequest r, CancellationToken ct)
    {
        new Validator().NotEmpty(r.Text, "text", 5000).Range(r.Speed, 0.5, 2, "speed").ThrowIfInvalid();

        var provider = registry.GetActive();
        var health = await provider.CheckHealthAsync(ct);
        if (!health.IsAvailable) throw new AppException(ErrorCode.TtsUnavailable, health.Message);

        storage.EnsureFreeSpace();
        var relative = storage.CreatePath("audio", ".wav");
        try
        {
            await provider.SynthesizeAsync(new TtsRequest(r.Text.Trim(), r.VoiceId, r.Speed), storage.GetFullPath(relative), ct);
        }
        catch
        {
            storage.Delete(relative);
            throw;
        }

        var asset = await media.RegisterAsync(relative, MediaType.Audio, MediaSource.Narration, currentUser.UserId, r.ProjectId, null, ct,
            originalFileName: "narration.wav");
        await db.SaveChangesAsync(ct);
        return asset.ToDto();
    }
}
