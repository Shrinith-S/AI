using Chitra.Application.Dtos;
using Chitra.Application.Media;
using Chitra.Application.Projects;
using Chitra.Application.Stories;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(ProjectService projects, SceneService scenes, MediaService media) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProjectSummaryDto>> List(CancellationToken ct) => projects.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<ProjectDetailDto> Get(Guid id, CancellationToken ct) => projects.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ProjectDetailDto>> Create(UpsertProjectRequest request, CancellationToken ct)
    {
        var created = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<ProjectDetailDto> Update(Guid id, UpsertProjectRequest request, CancellationToken ct) => projects.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Adds a generation's outputs or a single asset (image, video, audio) to the project.</summary>
    [HttpPost("{id:guid}/assets")]
    public async Task<IActionResult> Attach(Guid id, AttachAssetRequest request, CancellationToken ct)
    {
        await projects.AttachAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/assets")]
    public Task<PagedResult<MediaAssetDto>> Assets(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default) =>
        media.ListAsync(null, id, page, pageSize, ct);

    [HttpPost("{id:guid}/scenes")]
    public async Task<ActionResult<SceneDto>> AddScene(Guid id, UpsertSceneRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await scenes.AddAsync(id, request, ct));

    [HttpPost("{id:guid}/scenes/reorder")]
    public Task<IReadOnlyList<SceneDto>> Reorder(Guid id, ReorderScenesRequest request, CancellationToken ct) => scenes.ReorderAsync(id, request, ct);
}

[ApiController]
[Route("api/scenes")]
public sealed class ScenesController(SceneService scenes) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public Task<SceneDto> Update(Guid id, UpsertSceneRequest request, CancellationToken ct) => scenes.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await scenes.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicate")]
    public Task<SceneDto> Duplicate(Guid id, CancellationToken ct) => scenes.DuplicateAsync(id, ct);

    /// <summary>Queues a keyframe image ("image"), a text-to-video clip ("video") or animates the keyframe ("animate").</summary>
    [HttpPost("{id:guid}/generate")]
    public async Task<ActionResult<GenerationDto>> Generate(Guid id, GenerateSceneRequest request, CancellationToken ct) =>
        Accepted(await scenes.GenerateAsync(id, request, ct));

    [HttpPut("{id:guid}/media")]
    public Task<SceneDto> SetMedia(Guid id, SetSceneMediaRequest request, CancellationToken ct) => scenes.SetMediaAsync(id, request, ct);

    /// <summary>Generates the voice-over for the scene narration with the local TTS engine.</summary>
    [HttpPost("{id:guid}/narration")]
    public Task<SceneDto> Narration(Guid id, [FromBody] NarrationRequest? request, CancellationToken ct) =>
        scenes.GenerateNarrationAsync(id, request?.VoiceId, ct);

    public sealed record NarrationRequest(string? VoiceId);
}

[ApiController]
[Route("api/stories")]
public sealed class StoriesController(StoryService stories) : ControllerBase
{
    /// <summary>Plans a story into editable scenes inside a new (or existing) project. No media is generated yet.</summary>
    [HttpPost("generate")]
    public async Task<ActionResult<ProjectDetailDto>> Generate(GenerateStoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await stories.GenerateAsync(request, ct));
}
