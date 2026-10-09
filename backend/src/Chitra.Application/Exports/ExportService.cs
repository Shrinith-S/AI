using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Media;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chitra.Application.Exports;

public sealed class ExportService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFfmpegService ffmpeg,
    IExportQueue queue,
    IRealtimeNotifier notifier)
{
    public static readonly string[] Transitions = ["none", "fade", "dissolve", "wipeleft", "wiperight", "slideleft", "slideright", "circleopen", "fadeblack", "fadewhite", "smoothleft"];

    public async Task<VideoExportDto> CreateAsync(Guid projectId, CreateExportRequest r, CancellationToken ct)
    {
        new Validator()
            .Require(r.Resolution is 480 or 720 or 1080, "resolution", "Resolution must be 480, 720 or 1080.")
            .Require(r.Fps is 16 or 24 or 25 or 30, "fps", "FPS must be 16, 24, 25 or 30.")
            .Require(Transitions.Contains(r.Transition), "transition", $"Transition must be one of: {string.Join(", ", Transitions)}.")
            .Range(r.TransitionSeconds, 0.1, 3, "transitionSeconds")
            .Range(r.MusicVolume, 0, 2, "musicVolume")
            .ThrowIfInvalid();

        var userId = currentUser.UserId;
        var project = await db.Projects.AsNoTracking().Include(p => p.Scenes)
                          .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
                      ?? throw new NotFoundException("Project", projectId);

        if (project.Scenes.Count == 0) throw new ValidationException("scenes", "The project has no scenes to export.");
        var missing = project.Scenes.Where(s => s.VideoAssetId is null && s.ImageAssetId is null).OrderBy(s => s.Order).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException("scenes",
                $"Scene(s) {string.Join(", ", missing.Select(s => s.Order + 1))} have no video or keyframe yet. Generate them before exporting.");
        }

        if (r.MusicAssetId is { } musicId)
        {
            var music = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == musicId && a.UserId == userId, ct)
                        ?? throw new NotFoundException("Music", musicId);
            if (music.Type != MediaType.Audio) throw new ValidationException("musicAssetId", "Background music must be an audio file.");
        }

        var status = await ffmpeg.GetStatusAsync(ct);
        if (!status.IsAvailable) throw new AppException(ErrorCode.FfmpegUnavailable, status.Message);

        var export = new VideoExport
        {
            UserId = userId,
            ProjectId = projectId,
            Resolution = r.Resolution,
            Fps = r.Fps,
            Transition = r.Transition,
            TransitionSeconds = r.TransitionSeconds,
            IncludeNarration = r.IncludeNarration,
            IncludeSubtitles = r.IncludeSubtitles,
            MusicAssetId = r.MusicAssetId,
            MusicVolume = r.MusicVolume,
            StatusMessage = "Queued",
        };
        db.VideoExports.Add(export);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(export.Id, ct);

        var dto = export.ToDto(null);
        await notifier.ExportUpdatedAsync(userId, dto, ct);
        return dto;
    }

    public async Task<IReadOnlyList<VideoExportDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        var exports = await db.VideoExports.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.UserId == currentUser.UserId)
            .OrderByDescending(e => e.CreatedAt).Take(20).ToListAsync(ct);
        var outputIds = exports.Select(e => e.OutputAssetId).OfType<Guid>().ToList();
        var outputs = await db.MediaAssets.AsNoTracking().Where(a => outputIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return exports.Select(e => e.ToDto(e.OutputAssetId is { } id ? outputs.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<VideoExportDto> GetAsync(Guid id, CancellationToken ct)
    {
        var export = await FindAsync(id, ct);
        var output = export.OutputAssetId is null ? null : await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == export.OutputAssetId, ct);
        return export.ToDto(output);
    }

    public async Task<VideoExportDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var export = await FindAsync(id, ct, tracking: true);
        if (!export.Status.IsTerminal())
        {
            export.Status = GenerationStatus.Cancelled;
            export.StatusMessage = "Cancelled by user";
            export.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            queue.TryCancelRunning(id);
        }

        var dto = export.ToDto(null);
        await notifier.ExportUpdatedAsync(export.UserId, dto, ct);
        return dto;
    }

    private async Task<VideoExport> FindAsync(Guid id, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.VideoExports : db.VideoExports.AsNoTracking();
        return await query.FirstOrDefaultAsync(e => e.Id == id && e.UserId == currentUser.UserId, ct)
               ?? throw new NotFoundException("Export", id);
    }
}

/// <summary>Renders one export with FFmpeg in the background and stores the MP4 as the project's final video.</summary>
public sealed class ExportProcessor(
    IAppDbContext db,
    IFfmpegService ffmpeg,
    IMediaStorage storage,
    MediaService media,
    IRealtimeNotifier notifier,
    ILogger<ExportProcessor> logger)
{
    public async Task ProcessAsync(Guid exportId, CancellationToken cancellation, CancellationToken shutdown)
    {
        var export = await db.VideoExports.FirstOrDefaultAsync(e => e.Id == exportId, shutdown);
        if (export is null || export.Status.IsTerminal()) return;

        var project = await db.Projects.Include(p => p.Scenes).FirstAsync(p => p.Id == export.ProjectId, shutdown);
        string? outputRelative = null;
        try
        {
            export.Status = GenerationStatus.Processing;
            export.StartedAt = DateTime.UtcNow;
            export.Progress = 0;
            export.StatusMessage = "Preparing scenes";
            await SaveAndNotifyAsync(export, null, cancellation);

            var scenes = project.Scenes.OrderBy(s => s.Order).ToList();
            var assetIds = scenes.SelectMany(s => new[] { s.VideoAssetId, s.ImageAssetId, s.NarrationAssetId }).Append(export.MusicAssetId).OfType<Guid>().ToList();
            var assets = await db.MediaAssets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellation);

            string PathOf(Guid id)
            {
                if (!assets.TryGetValue(id, out var asset) || !storage.Exists(asset.RelativePath))
                {
                    throw new AppException(ErrorCode.Validation, "A scene's media file is missing from storage. Regenerate that scene and export again.");
                }

                return storage.GetFullPath(asset.RelativePath);
            }

            var clips = scenes.Select(s =>
            {
                var isVideo = s.VideoAssetId is not null;
                var narration = export.IncludeNarration && s.NarrationAssetId is { } n && assets.ContainsKey(n) ? assets[n] : null;

                // Never cut a voice-over short: stretch the shot (holding the last frame) to fit it.
                var duration = Math.Max(s.DurationSeconds, (narration?.DurationSeconds ?? 0) + 0.3);
                return new CompositionClip(
                    PathOf(isVideo ? s.VideoAssetId!.Value : s.ImageAssetId!.Value),
                    !isVideo,
                    Math.Round(duration, 2),
                    narration is null ? null : PathOf(narration.Id),
                    export.IncludeSubtitles ? s.Narration : null);
            }).ToList();

            var height = export.Resolution;
            var width = (int)Math.Round(height * project.Width / (double)project.Height / 2) * 2;
            outputRelative = storage.CreatePath("projects", ".mp4");
            storage.EnsureFreeSpace(200L * 1024 * 1024);

            var lastReported = -1;
            var lastNotify = DateTime.MinValue;
            var progress = new Progress<int>(p =>
            {
                if (p == lastReported || DateTime.UtcNow - lastNotify < TimeSpan.FromMilliseconds(500) && p < 100) return;
                lastReported = p;
                lastNotify = DateTime.UtcNow;
                export.Progress = Math.Min(p, 99);
                export.StatusMessage = p < 60 ? "Rendering scenes" : p < 80 ? "Joining scenes" : "Mixing audio and subtitles";
                _ = notifier.ExportUpdatedAsync(export.UserId, export.ToDto(null), CancellationToken.None);
            });

            await ffmpeg.ComposeAsync(new VideoCompositionRequest
            {
                Clips = clips,
                OutputPath = storage.GetFullPath(outputRelative),
                Width = width,
                Height = height,
                Fps = export.Fps,
                Transition = export.Transition,
                TransitionSeconds = export.TransitionSeconds,
                MusicPath = export.MusicAssetId is { } musicId ? PathOf(musicId) : null,
                MusicVolume = export.MusicVolume,
                BurnSubtitles = export.IncludeSubtitles,
            }, progress, cancellation);

            var asset = await media.RegisterAsync(outputRelative, MediaType.Video, MediaSource.Export, export.UserId, project.Id, null,
                CancellationToken.None, originalFileName: $"{Slug(project.Name)}-{export.Resolution}p.mp4");
            export.OutputAssetId = asset.Id;
            export.Status = GenerationStatus.Completed;
            export.Progress = 100;
            export.StatusMessage = "Completed";
            export.CompletedAt = DateTime.UtcNow;
            project.FinalVideoAssetId = asset.Id;
            project.Touch();
            await SaveAndNotifyAsync(export, asset, CancellationToken.None);
            logger.LogInformation("Export {ExportId} finished: {Path}", export.Id, outputRelative);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            DeletePartial(outputRelative);
            export.Status = GenerationStatus.Queued;
            export.StatusMessage = "Interrupted by shutdown; will restart";
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            DeletePartial(outputRelative);
            export.Status = GenerationStatus.Cancelled;
            export.StatusMessage = "Cancelled by user";
            export.CompletedAt = DateTime.UtcNow;
            await SaveAndNotifyAsync(export, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            DeletePartial(outputRelative);
            if (ex is not AppException) logger.LogError(ex, "Export {ExportId} failed", export.Id);
            export.Status = GenerationStatus.Failed;
            export.ErrorMessage = ex is AppException ? ex.Message : $"Export failed: {ex.Message}";
            export.StatusMessage = "Failed";
            export.CompletedAt = DateTime.UtcNow;
            await SaveAndNotifyAsync(export, null, CancellationToken.None);
        }
    }

    private async Task SaveAndNotifyAsync(VideoExport export, MediaAsset? output, CancellationToken ct)
    {
        export.Touch();
        await db.SaveChangesAsync(ct);
        await notifier.ExportUpdatedAsync(export.UserId, export.ToDto(output), ct);
    }

    private void DeletePartial(string? relative)
    {
        if (relative is null) return;
        try
        {
            storage.Delete(relative);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not delete partial export {Path}", relative);
        }
    }

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? "export" : slug[..Math.Min(slug.Length, 60)];
    }
}
