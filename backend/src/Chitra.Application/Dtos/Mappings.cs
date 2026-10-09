using Chitra.Domain.Entities;

namespace Chitra.Application.Dtos;

public static class Mappings
{
    public static MediaAssetDto ToDto(this MediaAsset asset) => new(
        asset.Id,
        asset.Type,
        asset.Source,
        asset.FileName,
        asset.OriginalFileName,
        asset.ContentType,
        asset.SizeBytes,
        asset.Width,
        asset.Height,
        asset.DurationSeconds,
        $"/api/media/{asset.Id}/content",
        $"/api/media/{asset.Id}/download",
        asset.ThumbnailPath is null ? null : $"/api/media/{asset.Id}/thumbnail",
        asset.ProjectId,
        asset.GenerationId,
        asset.CreatedAt);

    public static GenerationDto ToDto(this Generation g) => new(
        g.Id,
        g.Type,
        g.Status,
        g.Progress,
        g.StatusMessage,
        g.Prompt,
        g.FinalPrompt,
        g.NegativePrompt,
        g.PromptEnhanced,
        g.ModelId,
        g.ProviderName,
        g.StyleId,
        g.GetCharacterIds(),
        g.Width,
        g.Height,
        g.Steps,
        g.CfgScale,
        g.Seed,
        g.BatchSize,
        g.Sampler,
        g.Scheduler,
        g.DurationSeconds,
        g.Fps,
        g.FrameCount,
        g.MotionStrength,
        g.CameraMovement,
        g.InputAssetId,
        g.ProjectId,
        g.SceneId,
        g.ParentGenerationId,
        g.CreatedAt,
        g.StartedAt,
        g.CompletedAt,
        g.ErrorCode,
        g.ErrorMessage,
        string.IsNullOrWhiteSpace(g.Warnings) ? [] : g.Warnings.Split('\n', StringSplitOptions.RemoveEmptyEntries),
        g.Outputs.OrderBy(o => o.CreatedAt).Select(o => o.ToDto()).ToList());

    public static CharacterDto ToDto(this Character c) => new(
        c.Id,
        c.Name,
        c.Description,
        c.Age,
        c.Gender,
        c.Clothing,
        c.Appearance,
        c.Personality,
        c.PromptOverride,
        c.PreferredSeed,
        c.ReferenceAsset?.ToDto(),
        c.BuildPrompt(),
        c.CreatedAt,
        c.UpdatedAt);

    public static StyleDto ToDto(this Style s) =>
        new(s.Id, s.Name, s.Description, s.PromptPrefix, s.PromptSuffix, s.NegativePrompt, s.IsBuiltIn);

    public static PromptTemplateDto ToDto(this PromptTemplate t) =>
        new(t.Id, t.Name, t.Category, t.Template, t.NegativePrompt, t.Description, t.IsBuiltIn);

    public static SceneGenerationState? ToSceneState(this Generation? g) =>
        g is null ? null : new SceneGenerationState(g.Id, g.Status, g.Progress, g.StatusMessage, g.ErrorMessage);

    public static VideoExportDto ToDto(this VideoExport e, MediaAsset? output) => new(
        e.Id,
        e.ProjectId,
        e.Status,
        e.Progress,
        e.StatusMessage,
        e.Resolution,
        e.Fps,
        e.Transition,
        e.TransitionSeconds,
        e.IncludeNarration,
        e.IncludeSubtitles,
        e.MusicAssetId,
        e.MusicVolume,
        output?.ToDto(),
        e.ErrorMessage,
        e.CreatedAt,
        e.StartedAt,
        e.CompletedAt);
}
