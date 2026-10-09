using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Media;
using Chitra.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController(MediaService media) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(MediaService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaService.MaxUploadBytes + 1024 * 1024)]
    public async Task<ActionResult<MediaAssetDto>> Upload(IFormFile? file, [FromForm] Guid? projectId, CancellationToken ct)
    {
        if (file is null) throw new ValidationException("file", "Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        var asset = await media.UploadAsync(stream, file.FileName, file.Length, projectId, ct);
        return CreatedAtAction(nameof(Get), new { id = asset.Id }, asset);
    }

    [HttpGet]
    public Task<PagedResult<MediaAssetDto>> List([FromQuery] MediaType? type, [FromQuery] Guid? projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 48, CancellationToken ct = default) =>
        media.ListAsync(type, projectId, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<MediaAssetDto> Get(Guid id, CancellationToken ct) => media.GetAsync(id, ct);

    /// <summary>Streams the file with HTTP range support, so videos can be scrubbed.</summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var file = await media.GetFileAsync(id, thumbnail: false, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(file.FullPath, file.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var file = await media.GetFileAsync(id, thumbnail: false, ct);
        return PhysicalFile(file.FullPath, file.ContentType, file.DownloadName, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> Thumbnail(Guid id, CancellationToken ct)
    {
        var file = await media.GetFileAsync(id, thumbnail: true, ct);
        Response.Headers.CacheControl = "private, max-age=604800";
        return PhysicalFile(file.FullPath, file.ContentType);
    }

    /// <summary>Saves the last frame of a video as an image (e.g. to start the next scene from it). Requires FFmpeg.</summary>
    [HttpPost("{id:guid}/last-frame")]
    public Task<MediaAssetDto> LastFrame(Guid id, CancellationToken ct) => media.ExtractLastFrameAsync(id, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await media.DeleteAsync(id, ct);
        return NoContent();
    }
}
