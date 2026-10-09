using Chitra.Application.Dtos;
using Chitra.Application.Generations;
using Chitra.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Controllers;

[ApiController]
[Route("api/generations")]
public sealed class GenerationsController(GenerationService generations) : ControllerBase
{
    /// <summary>Queues a text-to-image generation. Returns immediately; progress arrives over SignalR.</summary>
    [HttpPost("image")]
    public async Task<ActionResult<GenerationDto>> CreateImage(CreateImageGenerationRequest request, CancellationToken ct)
    {
        var result = await generations.CreateImageAsync(request, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("video")]
    public async Task<ActionResult<GenerationDto>> CreateVideo(CreateVideoGenerationRequest request, CancellationToken ct)
    {
        var type = request.InputAssetId is null ? GenerationType.TextToVideo : GenerationType.ImageToVideo;
        var result = await generations.CreateVideoAsync(request, type, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("image-to-video")]
    public async Task<ActionResult<GenerationDto>> CreateImageToVideo(CreateVideoGenerationRequest request, CancellationToken ct)
    {
        var result = await generations.CreateVideoAsync(request, GenerationType.ImageToVideo, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet]
    public Task<PagedResult<GenerationDto>> List([FromQuery] GenerationQuery query, CancellationToken ct) =>
        generations.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<GenerationDto> Get(Guid id, CancellationToken ct) => generations.GetAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<GenerationDto> Cancel(Guid id, CancellationToken ct) => generations.CancelAsync(id, ct);

    /// <summary>Runs the same settings again. With newSeed=false the result is reproduced exactly.</summary>
    [HttpPost("{id:guid}/regenerate")]
    public async Task<ActionResult<GenerationDto>> Regenerate(Guid id, [FromBody] RegenerateRequest? request, CancellationToken ct)
    {
        var result = await generations.RegenerateAsync(id, request?.NewSeed ?? false, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>Same prompt and settings with a new random seed.</summary>
    [HttpPost("{id:guid}/variation")]
    public async Task<ActionResult<GenerationDto>> Variation(Guid id, CancellationToken ct)
    {
        var result = await generations.RegenerateAsync(id, newSeed: true, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await generations.DeleteAsync(id, ct);
        return NoContent();
    }
}
