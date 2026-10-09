using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chitra.Application.Projects;

public sealed class ProjectService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<ProjectSummaryDto>> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var projects = await db.Projects.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.CoverAssetId,
                p.CreatedAt,
                p.UpdatedAt,
                Scenes = p.Scenes.Count,
                Characters = p.Characters.Count,
                Assets = p.Assets.Count,
                FirstImage = p.Scenes.OrderBy(s => s.Order).Select(s => s.ImageAssetId).FirstOrDefault(id => id != null),
            })
            .ToListAsync(ct);

        return projects.Select(p =>
        {
            var cover = p.CoverAssetId ?? p.FirstImage;
            return new ProjectSummaryDto(p.Id, p.Name, p.Description, p.Scenes, p.Characters, p.Assets,
                cover is null ? null : $"/api/media/{cover}/content", p.CreatedAt, p.UpdatedAt);
        }).ToList();
    }

    public Task<ProjectDetailDto> GetAsync(Guid id, CancellationToken ct) => BuildDetailAsync(id, ct);

    public async Task<ProjectDetailDto> CreateAsync(UpsertProjectRequest r, CancellationToken ct)
    {
        var project = new Project { UserId = currentUser.UserId };
        await ApplyAsync(project, r, ct);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return await BuildDetailAsync(project.Id, ct);
    }

    public async Task<ProjectDetailDto> UpdateAsync(Guid id, UpsertProjectRequest r, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Characters).FirstOrDefaultAsync(p => p.Id == id && p.UserId == currentUser.UserId, ct)
                      ?? throw new NotFoundException("Project", id);
        await ApplyAsync(project, r, ct);
        project.Touch();
        await db.SaveChangesAsync(ct);
        return await BuildDetailAsync(id, ct);
    }

    /// <summary>Deletes the project and its scenes. Generated media is kept in History.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.UserId == currentUser.UserId, ct)
                      ?? throw new NotFoundException("Project", id);
        await db.Generations.Where(g => g.ProjectId == id).ExecuteUpdateAsync(s => s
            .SetProperty(g => g.ProjectId, (Guid?)null)
            .SetProperty(g => g.SceneId, (Guid?)null), ct);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a generation's outputs (or a single asset) to the project.</summary>
    public async Task AttachAsync(Guid projectId, AttachAssetRequest r, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
                      ?? throw new NotFoundException("Project", projectId);

        if (r.GenerationId is { } generationId)
        {
            var generation = await db.Generations.Include(g => g.Outputs).FirstOrDefaultAsync(g => g.Id == generationId && g.UserId == userId, ct)
                             ?? throw new NotFoundException("Generation", generationId);
            generation.ProjectId = projectId;
            foreach (var output in generation.Outputs) output.ProjectId = projectId;
        }
        else if (r.AssetId is { } assetId)
        {
            var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId && a.UserId == userId, ct)
                        ?? throw new NotFoundException("Media asset", assetId);
            asset.ProjectId = projectId;
        }
        else
        {
            throw new ValidationException("assetId", "Provide an assetId or a generationId.");
        }

        project.Touch();
        await db.SaveChangesAsync(ct);
    }

    internal async Task<ProjectDetailDto> BuildDetailAsync(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
                          .Include(p => p.Characters).ThenInclude(c => c.ReferenceAsset)
                          .Include(p => p.Scenes).ThenInclude(s => s.Characters)
                          .FirstOrDefaultAsync(p => p.Id == id && p.UserId == currentUser.UserId, ct)
                      ?? throw new NotFoundException("Project", id);

        var scenes = project.Scenes.OrderBy(s => s.Order).ToList();
        var assetIds = scenes.SelectMany(s => new[] { s.ImageAssetId, s.VideoAssetId, s.NarrationAssetId })
            .Append(project.FinalVideoAssetId).OfType<Guid>().Distinct().ToList();
        var generationIds = scenes.SelectMany(s => new[] { s.LastImageGenerationId, s.LastVideoGenerationId }).OfType<Guid>().Distinct().ToList();

        var assets = await db.MediaAssets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var generations = await db.Generations.AsNoTracking().Where(g => generationIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        MediaAssetDto? Asset(Guid? assetId) => assetId is { } x && assets.TryGetValue(x, out var a) ? a.ToDto() : null;
        Generation? Gen(Guid? generationId) => generationId is { } x && generations.TryGetValue(x, out var g) ? g : null;

        var sceneDtos = scenes.Select(s => ToDto(s, Asset(s.ImageAssetId), Asset(s.VideoAssetId), Asset(s.NarrationAssetId),
            Gen(s.LastImageGenerationId), Gen(s.LastVideoGenerationId))).ToList();

        return new ProjectDetailDto(project.Id, project.Name, project.Description, project.StoryText, project.StyleId,
            project.ImageModelId, project.VideoModelId, project.Width, project.Height, project.Fps, Asset(project.FinalVideoAssetId),
            sceneDtos, project.Characters.OrderBy(c => c.Name).Select(c => c.ToDto()).ToList(), project.CreatedAt, project.UpdatedAt);
    }

    internal static SceneDto ToDto(Scene s, MediaAssetDto? image, MediaAssetDto? video, MediaAssetDto? narration, Generation? imageGen, Generation? videoGen) =>
        new(s.Id, s.ProjectId, s.Order, s.Title, s.Description, s.Location, s.Action, s.Mood, s.ImagePrompt, s.VideoPrompt,
            s.NegativePrompt, s.CameraMovement, s.DurationSeconds, s.Narration, s.Characters.Select(c => c.Id).ToList(),
            image, video, narration, imageGen.ToSceneState(), videoGen.ToSceneState());

    private async Task ApplyAsync(Project p, UpsertProjectRequest r, CancellationToken ct)
    {
        new Validator()
            .NotEmpty(r.Name, "name", 200)
            .Range(r.Width, 128, 2048, "width")
            .Range(r.Height, 128, 2048, "height")
            .Require(r.Fps is null or 8 or 12 or 16 or 24 or 25 or 30, "fps", "FPS must be 8, 12, 16, 24, 25 or 30.")
            .ThrowIfInvalid();

        if (r.StyleId is { } styleId && !await db.Styles.AnyAsync(s => s.Id == styleId && (s.UserId == null || s.UserId == currentUser.UserId), ct))
        {
            throw new NotFoundException("Style", styleId);
        }

        p.Name = r.Name.Trim();
        p.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        p.StoryText = string.IsNullOrWhiteSpace(r.StoryText) ? p.StoryText : r.StoryText.Trim();
        p.StyleId = r.StyleId;
        p.ImageModelId = r.ImageModelId ?? p.ImageModelId;
        p.VideoModelId = r.VideoModelId ?? p.VideoModelId;
        p.Width = r.Width ?? p.Width;
        p.Height = r.Height ?? p.Height;
        p.Fps = r.Fps ?? p.Fps;

        if (r.CharacterIds is not null)
        {
            var ids = r.CharacterIds.Distinct().ToList();
            var characters = await db.Characters.Where(c => ids.Contains(c.Id) && c.UserId == currentUser.UserId).ToListAsync(ct);
            if (characters.Count != ids.Count) throw new NotFoundException("Character", string.Join(", ", ids.Except(characters.Select(c => c.Id))));
            p.Characters.Clear();
            foreach (var c in characters) p.Characters.Add(c);
        }
    }
}
