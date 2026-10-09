using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Tts;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Generations;
using Chitra.Application.Media;
using Chitra.Application.Settings;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Chitra.Application.Projects;

public sealed class SceneService(
    IAppDbContext db,
    ICurrentUser currentUser,
    GenerationService generations,
    IRuntimeSettings settings,
    ITtsProviderRegistry tts,
    IMediaStorage storage,
    MediaService media,
    ProjectService projects)
{
    public async Task<SceneDto> AddAsync(Guid projectId, UpsertSceneRequest r, CancellationToken ct)
    {
        Validate(r);
        var project = await db.Projects.Include(p => p.Scenes).FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == currentUser.UserId, ct)
                      ?? throw new NotFoundException("Project", projectId);

        var ordered = project.Scenes.OrderBy(s => s.Order).ToList();
        var position = Math.Clamp(r.Order ?? ordered.Count, 0, ordered.Count);
        var scene = new Scene { ProjectId = projectId };
        await ApplyAsync(scene, r, ct);
        ordered.Insert(position, scene);
        Renumber(ordered);
        db.Scenes.Add(scene);
        project.Touch();
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(scene.Id, ct);
    }

    public async Task<SceneDto> UpdateAsync(Guid id, UpsertSceneRequest r, CancellationToken ct)
    {
        Validate(r);
        var scene = await FindAsync(id, ct);
        await ApplyAsync(scene, r, ct);
        scene.Touch();
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var scene = await FindAsync(id, ct);
        var siblings = await db.Scenes.Where(s => s.ProjectId == scene.ProjectId && s.Id != id).OrderBy(s => s.Order).ToListAsync(ct);
        await db.Generations.Where(g => g.SceneId == id).ExecuteUpdateAsync(s => s.SetProperty(g => g.SceneId, (Guid?)null), ct);
        db.Scenes.Remove(scene);
        Renumber(siblings);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Copies a scene (text, characters and current media) and inserts it right after the original.</summary>
    public async Task<SceneDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await FindAsync(id, ct);
        var siblings = await db.Scenes.Where(s => s.ProjectId == source.ProjectId).OrderBy(s => s.Order).ToListAsync(ct);
        var copy = new Scene
        {
            ProjectId = source.ProjectId,
            Title = source.Title.Length > 190 ? source.Title : $"{source.Title} (copy)",
            Description = source.Description,
            Location = source.Location,
            Action = source.Action,
            Mood = source.Mood,
            ImagePrompt = source.ImagePrompt,
            VideoPrompt = source.VideoPrompt,
            NegativePrompt = source.NegativePrompt,
            CameraMovement = source.CameraMovement,
            DurationSeconds = source.DurationSeconds,
            Narration = source.Narration,
            ImageAssetId = source.ImageAssetId,
            VideoAssetId = source.VideoAssetId,
            NarrationAssetId = source.NarrationAssetId,
        };
        foreach (var c in source.Characters) copy.Characters.Add(c);

        siblings.Insert(siblings.IndexOf(siblings.First(s => s.Id == id)) + 1, copy);
        Renumber(siblings);
        db.Scenes.Add(copy);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(copy.Id, ct);
    }

    public async Task<IReadOnlyList<SceneDto>> ReorderAsync(Guid projectId, ReorderScenesRequest r, CancellationToken ct)
    {
        var scenes = await db.Scenes.Where(s => s.ProjectId == projectId && s.Project!.UserId == currentUser.UserId).ToListAsync(ct);
        if (scenes.Count == 0 && !await db.Projects.AnyAsync(p => p.Id == projectId && p.UserId == currentUser.UserId, ct))
        {
            throw new NotFoundException("Project", projectId);
        }

        if (r.SceneIds.Count != scenes.Count || r.SceneIds.Distinct().Count() != scenes.Count || r.SceneIds.Any(id => scenes.All(s => s.Id != id)))
        {
            throw new ValidationException("sceneIds", "Send every scene id of the project exactly once, in the new order.");
        }

        for (var i = 0; i < r.SceneIds.Count; i++)
        {
            scenes.First(s => s.Id == r.SceneIds[i]).Order = i;
        }

        await db.SaveChangesAsync(ct);
        return (await projects.BuildDetailAsync(projectId, ct)).Scenes;
    }

    /// <summary>
    /// image = keyframe from the image prompt; video = text-to-video from the video prompt;
    /// animate = image-to-video from the scene keyframe (best character consistency).
    /// </summary>
    public async Task<GenerationDto> GenerateAsync(Guid id, GenerateSceneRequest r, CancellationToken ct)
    {
        var scene = await FindAsync(id, ct);
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == scene.ProjectId, ct);
        var characterIds = scene.Characters.Select(c => c.Id).ToList();
        var kind = r.Kind.Trim().ToLowerInvariant();

        GenerationDto result;
        switch (kind)
        {
            case "image":
            {
                var (w, h) = KeyframeSize(project.Width, project.Height);
                result = await generations.CreateAsync(new GenerationSpec
                {
                    Type = GenerationType.TextToImage,
                    Prompt = Require(scene.ImagePrompt, "image prompt"),
                    NegativePrompt = scene.NegativePrompt,
                    ModelId = r.ModelId ?? project.ImageModelId,
                    Width = w,
                    Height = h,
                    Steps = r.Steps,
                    CfgScale = r.CfgScale,
                    Seed = r.Seed,
                    StyleId = project.StyleId,
                    CharacterIds = characterIds,
                    EnhancePrompt = r.EnhancePrompt,
                    ProjectId = project.Id,
                    SceneId = scene.Id,
                }, ct);
                scene.LastImageGenerationId = result.Id;
                break;
            }

            case "video":
            case "animate":
            {
                var animate = kind == "animate";
                if (animate && scene.ImageAssetId is null)
                {
                    throw new ValidationException("kind", "Generate or choose a keyframe image for this scene before animating it.");
                }

                var current = settings.Current;
                result = await generations.CreateAsync(new GenerationSpec
                {
                    Type = animate ? GenerationType.ImageToVideo : GenerationType.TextToVideo,
                    Prompt = Require(scene.VideoPrompt, "video prompt"),
                    NegativePrompt = scene.NegativePrompt,
                    ModelId = r.ModelId ?? project.VideoModelId ?? (animate ? current.DefaultImageToVideoModel : current.DefaultVideoModel),
                    Width = project.Width,
                    Height = project.Height,
                    Fps = project.Fps,
                    DurationSeconds = Math.Clamp(scene.DurationSeconds, 1, 20),
                    Steps = r.Steps,
                    CfgScale = r.CfgScale,
                    Seed = r.Seed,
                    MotionStrength = r.MotionStrength,
                    CameraMovement = scene.CameraMovement,
                    StyleId = project.StyleId,
                    CharacterIds = characterIds,
                    EnhancePrompt = r.EnhancePrompt,
                    ProjectId = project.Id,
                    SceneId = scene.Id,
                    InputAssetId = animate ? scene.ImageAssetId : null,
                }, ct);
                scene.LastVideoGenerationId = result.Id;
                break;
            }

            default:
                throw new ValidationException("kind", "Kind must be 'image', 'video' or 'animate'.");
        }

        scene.Touch();
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Chooses which existing image/video the scene uses (e.g. an earlier take).</summary>
    public async Task<SceneDto> SetMediaAsync(Guid id, SetSceneMediaRequest r, CancellationToken ct)
    {
        var scene = await FindAsync(id, ct);
        if (r.ImageAssetId is { } imageId) scene.ImageAssetId = await RequireAssetAsync(imageId, MediaType.Image, ct);
        if (r.VideoAssetId is { } videoId) scene.VideoAssetId = await RequireAssetAsync(videoId, MediaType.Video, ct);
        scene.Touch();
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(id, ct);
    }

    /// <summary>Synthesizes the scene narration with the local TTS engine and stores it as the scene's voice-over.</summary>
    public async Task<SceneDto> GenerateNarrationAsync(Guid id, string? voiceId, CancellationToken ct)
    {
        var scene = await FindAsync(id, ct);
        var text = Require(scene.Narration, "narration");
        var provider = tts.GetActive();
        var health = await provider.CheckHealthAsync(ct);
        if (!health.IsAvailable) throw new AppException(ErrorCode.TtsUnavailable, health.Message);

        storage.EnsureFreeSpace();
        var relative = storage.CreatePath("audio", ".wav");
        try
        {
            await provider.SynthesizeAsync(new TtsRequest(text, voiceId), storage.GetFullPath(relative), ct);
        }
        catch
        {
            storage.Delete(relative);
            throw;
        }

        var asset = await media.RegisterAsync(relative, MediaType.Audio, MediaSource.Narration, currentUser.UserId, scene.ProjectId, null, ct,
            originalFileName: $"scene-{scene.Order + 1}-narration.wav");
        scene.NarrationAssetId = asset.Id;
        scene.Touch();
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(id, ct);
    }

    /// <summary>Keyframes match the project's aspect ratio with a ~1024px long side, which image models handle well.</summary>
    internal static (int Width, int Height) KeyframeSize(int width, int height)
    {
        const int longSide = 1024;
        return width >= height
            ? (longSide, (int)Math.Round(longSide * height / (double)width / 64) * 64)
            : ((int)Math.Round(longSide * width / (double)height / 64) * 64, longSide);
    }

    private async Task<Guid> RequireAssetAsync(Guid id, MediaType type, CancellationToken ct)
    {
        var asset = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, ct)
                    ?? throw new NotFoundException("Media asset", id);
        if (asset.Type != type) throw new ValidationException("assetId", $"Expected a {type.ToString().ToLowerInvariant()}.");
        return asset.Id;
    }

    private async Task<SceneDto> GetDtoAsync(Guid sceneId, CancellationToken ct)
    {
        var projectId = await db.Scenes.Where(s => s.Id == sceneId).Select(s => s.ProjectId).FirstAsync(ct);
        return (await projects.BuildDetailAsync(projectId, ct)).Scenes.First(s => s.Id == sceneId);
    }

    private async Task<Scene> FindAsync(Guid id, CancellationToken ct) =>
        await db.Scenes.Include(s => s.Characters).FirstOrDefaultAsync(s => s.Id == id && s.Project!.UserId == currentUser.UserId, ct)
        ?? throw new NotFoundException("Scene", id);

    private async Task ApplyAsync(Scene s, UpsertSceneRequest r, CancellationToken ct)
    {
        s.Title = r.Title.Trim();
        s.Description = Clean(r.Description);
        s.Location = Clean(r.Location);
        s.Action = Clean(r.Action);
        s.Mood = Clean(r.Mood);
        s.ImagePrompt = r.ImagePrompt.Trim();
        s.VideoPrompt = r.VideoPrompt.Trim();
        s.NegativePrompt = Clean(r.NegativePrompt);
        s.CameraMovement = Clean(r.CameraMovement);
        s.DurationSeconds = Math.Round(r.DurationSeconds, 1);
        s.Narration = Clean(r.Narration);

        if (r.CharacterIds is not null)
        {
            var ids = r.CharacterIds.Distinct().ToList();
            var characters = await db.Characters.Where(c => ids.Contains(c.Id) && c.UserId == currentUser.UserId).ToListAsync(ct);
            if (characters.Count != ids.Count) throw new NotFoundException("Character", string.Join(", ", ids.Except(characters.Select(c => c.Id))));
            s.Characters.Clear();
            foreach (var c in characters) s.Characters.Add(c);
        }
    }

    private static void Validate(UpsertSceneRequest r) => new Validator()
        .NotEmpty(r.Title, "title", 200)
        .Range(r.DurationSeconds, 1, 20, "durationSeconds")
        .Require(r.ImagePrompt.Length <= 4000 && r.VideoPrompt.Length <= 4000, "prompts", "Prompts must be at most 4000 characters.")
        .Require((r.Narration?.Length ?? 0) <= 2000, "narration", "Narration must be at most 2000 characters.")
        .ThrowIfInvalid();

    private static void Renumber(List<Scene> ordered)
    {
        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
    }

    private static string Require(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(what, $"This scene has no {what}. Add one first.") : value;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
