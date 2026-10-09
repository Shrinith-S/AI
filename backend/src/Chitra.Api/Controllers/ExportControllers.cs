using Chitra.Application.Dtos;
using Chitra.Application.Exports;
using Chitra.Application.Tts;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Controllers;

[ApiController]
public sealed class ExportsController(ExportService exports) : ControllerBase
{
    /// <summary>Queues a final video export (FFmpeg). Progress arrives over SignalR as "exportUpdated".</summary>
    [HttpPost("api/projects/{projectId:guid}/exports")]
    public async Task<ActionResult<VideoExportDto>> Create(Guid projectId, CreateExportRequest request, CancellationToken ct) =>
        Accepted(await exports.CreateAsync(projectId, request, ct));

    [HttpGet("api/projects/{projectId:guid}/exports")]
    public Task<IReadOnlyList<VideoExportDto>> List(Guid projectId, CancellationToken ct) => exports.ListAsync(projectId, ct);

    [HttpGet("api/exports/{id:guid}")]
    public Task<VideoExportDto> Get(Guid id, CancellationToken ct) => exports.GetAsync(id, ct);

    [HttpPost("api/exports/{id:guid}/cancel")]
    public Task<VideoExportDto> Cancel(Guid id, CancellationToken ct) => exports.CancelAsync(id, ct);
}

[ApiController]
[Route("api/tts")]
public sealed class TtsController(TtsService tts) : ControllerBase
{
    [HttpGet("status")]
    public Task<TtsStatusDto> Status(CancellationToken ct) => tts.GetStatusAsync(ct);

    /// <summary>Synthesizes speech locally and returns the stored WAV asset.</summary>
    [HttpPost("synthesize")]
    public async Task<ActionResult<MediaAssetDto>> Synthesize(SynthesizeSpeechRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tts.SynthesizeAsync(request, ct));
}
