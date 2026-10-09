using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chitra.Application.Media;

public sealed record MediaFile(string FullPath, string ContentType, string DownloadName);

public sealed class MediaService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IMediaStorage storage,
    IFfmpegService ffmpeg,
    ILogger<MediaService> logger)
{
    public const long MaxUploadBytes = 200L * 1024 * 1024;

    private static readonly Dictionary<string, (MediaType Type, string ContentType)> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = (MediaType.Image, "image/png"),
        [".jpg"] = (MediaType.Image, "image/jpeg"),
        [".jpeg"] = (MediaType.Image, "image/jpeg"),
        [".webp"] = (MediaType.Image, "image/webp"),
        [".gif"] = (MediaType.Image, "image/gif"),
        [".mp4"] = (MediaType.Video, "video/mp4"),
        [".webm"] = (MediaType.Video, "video/webm"),
        [".mov"] = (MediaType.Video, "video/quicktime"),
        [".mp3"] = (MediaType.Audio, "audio/mpeg"),
        [".wav"] = (MediaType.Audio, "audio/wav"),
        [".ogg"] = (MediaType.Audio, "audio/ogg"),
        [".m4a"] = (MediaType.Audio, "audio/mp4"),
        [".flac"] = (MediaType.Audio, "audio/flac"),
    };

    public static (MediaType Type, string ContentType)? Classify(string fileName) =>
        KnownTypes.TryGetValue(Path.GetExtension(fileName), out var info) ? info : null;

    public static string CategoryFor(MediaType type, MediaSource source) => source switch
    {
        MediaSource.Uploaded => "uploads",
        MediaSource.Export => "projects",
        _ => type switch
        {
            MediaType.Image => "images",
            MediaType.Video => "videos",
            _ => "audio",
        },
    };

    public async Task<MediaAssetDto> UploadAsync(Stream content, string fileName, long length, Guid? projectId, CancellationToken ct)
    {
        var info = Classify(fileName)
                   ?? throw new ValidationException("file", $"Unsupported file type '{Path.GetExtension(fileName)}'. Use PNG, JPG, WEBP, MP4, WEBM, MP3, WAV, OGG, M4A or FLAC.");
        if (length <= 0) throw new ValidationException("file", "The file is empty.");
        if (length > MaxUploadBytes) throw new ValidationException("file", $"Files must be smaller than {MaxUploadBytes / 1024 / 1024} MB.");

        if (projectId is { } pid && !await db.Projects.AnyAsync(p => p.Id == pid && p.UserId == currentUser.UserId, ct))
        {
            throw new NotFoundException("Project", pid);
        }

        var asset = await CreateAssetAsync(content, Path.GetExtension(fileName), info.Type, MediaSource.Uploaded,
            currentUser.UserId, projectId, null, ct, originalFileName: Path.GetFileName(fileName));
        await db.SaveChangesAsync(ct);
        return asset.ToDto();
    }

    /// <summary>Stores a stream as a new asset (added to the context, not yet saved).</summary>
    public async Task<MediaAsset> CreateAssetAsync(
        Stream content,
        string extension,
        MediaType type,
        MediaSource source,
        Guid userId,
        Guid? projectId,
        Guid? generationId,
        CancellationToken ct,
        string? originalFileName = null)
    {
        storage.EnsureFreeSpace();
        var stored = await storage.SaveAsync(content, CategoryFor(type, source), extension, ct);
        return await RegisterAsync(stored.RelativePath, type, source, userId, projectId, generationId, ct, originalFileName);
    }

    /// <summary>Registers a file already written under the storage root (added to the context, not yet saved).</summary>
    public async Task<MediaAsset> RegisterAsync(
        string relativePath,
        MediaType type,
        MediaSource source,
        Guid userId,
        Guid? projectId,
        Guid? generationId,
        CancellationToken ct,
        string? originalFileName = null)
    {
        var fullPath = storage.GetFullPath(relativePath);
        var asset = new MediaAsset
        {
            UserId = userId,
            ProjectId = projectId,
            GenerationId = generationId,
            Type = type,
            Source = source,
            FileName = Path.GetFileName(relativePath),
            OriginalFileName = originalFileName,
            RelativePath = relativePath,
            ContentType = Classify(relativePath)?.ContentType ?? "application/octet-stream",
            SizeBytes = new FileInfo(fullPath).Length,
        };

        try
        {
            var probe = await ffmpeg.ProbeAsync(fullPath, ct);
            if (probe is not null)
            {
                asset.Width = probe.Width;
                asset.Height = probe.Height;
                asset.DurationSeconds = type == MediaType.Image ? null : probe.DurationSeconds;
            }

            if (type != MediaType.Audio)
            {
                var thumbRelative = storage.CreatePath("thumbnails", ".jpg");
                if (await ffmpeg.TryCreateThumbnailAsync(fullPath, storage.GetFullPath(thumbRelative), type, ct))
                {
                    asset.ThumbnailPath = thumbRelative;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Probing and thumbnails are best-effort; the asset itself is fine without them.
            logger.LogDebug(ex, "Could not probe or thumbnail {Path}", relativePath);
        }

        db.MediaAssets.Add(asset);
        return asset;
    }

    public async Task<MediaAssetDto> GetAsync(Guid id, CancellationToken ct) => (await FindOwnedAsync(id, ct)).ToDto();

    public async Task<PagedResult<MediaAssetDto>> ListAsync(MediaType? type, Guid? projectId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.MediaAssets.AsNoTracking().Where(a => a.UserId == currentUser.UserId);
        if (type is not null) query = query.Where(a => a.Type == type);
        if (projectId is not null) query = query.Where(a => a.ProjectId == projectId);
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<MediaAssetDto>(items.Select(a => a.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<MediaFile> GetFileAsync(Guid id, bool thumbnail, CancellationToken ct)
    {
        var asset = await FindOwnedAsync(id, ct);
        var relative = thumbnail ? asset.ThumbnailPath ?? throw new NotFoundException("Thumbnail", id) : asset.RelativePath;
        if (!storage.Exists(relative))
        {
            throw new AppException(ErrorCode.NotFound, $"The file for asset '{id}' is missing from storage ({relative}).");
        }

        var contentType = thumbnail ? "image/jpeg" : asset.ContentType;
        var downloadName = asset.OriginalFileName ?? asset.FileName;
        return new MediaFile(storage.GetFullPath(relative), contentType, downloadName);
    }

    /// <summary>Saves the final frame of a video as a new image, e.g. to start the next scene from it.</summary>
    public async Task<MediaAssetDto> ExtractLastFrameAsync(Guid videoAssetId, CancellationToken ct)
    {
        var video = await FindOwnedAsync(videoAssetId, ct);
        if (video.Type != MediaType.Video) throw new ValidationException("assetId", "Only videos have a last frame.");

        var relative = storage.CreatePath("images", ".png");
        await ffmpeg.ExtractLastFrameAsync(storage.GetFullPath(video.RelativePath), storage.GetFullPath(relative), ct);
        var asset = await RegisterAsync(relative, MediaType.Image, MediaSource.Generated, video.UserId, video.ProjectId, null, ct,
            originalFileName: $"{Path.GetFileNameWithoutExtension(video.FileName)}-last-frame.png");
        await db.SaveChangesAsync(ct);
        return asset.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct) => await DeleteAssetAsync(await FindOwnedAsync(id, ct, tracking: true), ct);

    /// <summary>Deletes files from disk, clears every reference to the asset and removes it.</summary>
    public async Task DeleteAssetAsync(MediaAsset asset, CancellationToken ct)
    {
        var id = (Guid?)asset.Id;
        await db.Scenes.Where(s => s.ImageAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImageAssetId, (Guid?)null), ct);
        await db.Scenes.Where(s => s.VideoAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.VideoAssetId, (Guid?)null), ct);
        await db.Scenes.Where(s => s.NarrationAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NarrationAssetId, (Guid?)null), ct);
        await db.Characters.Where(c => c.ReferenceAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReferenceAssetId, (Guid?)null), ct);
        await db.Projects.Where(p => p.FinalVideoAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FinalVideoAssetId, (Guid?)null), ct);
        await db.Projects.Where(p => p.CoverAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CoverAssetId, (Guid?)null), ct);
        await db.Generations.Where(g => g.OutputAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.OutputAssetId, (Guid?)null), ct);
        await db.VideoExports.Where(e => e.OutputAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.OutputAssetId, (Guid?)null), ct);
        await db.VideoExports.Where(e => e.MusicAssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MusicAssetId, (Guid?)null), ct);

        TryDeleteFile(asset.RelativePath);
        if (asset.ThumbnailPath is not null) TryDeleteFile(asset.ThumbnailPath);

        db.MediaAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
    }

    internal async Task<MediaAsset> FindOwnedAsync(Guid id, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.MediaAssets : db.MediaAssets.AsNoTracking();
        return await query.FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, ct)
               ?? throw new NotFoundException("Media asset", id);
    }

    private void TryDeleteFile(string relativePath)
    {
        try
        {
            storage.Delete(relativePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete {Path}", relativePath);
        }
    }
}
