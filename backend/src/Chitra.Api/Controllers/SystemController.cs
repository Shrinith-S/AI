using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Models;
using Chitra.Application.Prompting;
using Chitra.Application.Settings;
using Chitra.Application.SystemStatus;
using Chitra.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(SystemStatusService status, ISystemInfoService systemInfo) : ControllerBase
{
    /// <summary>Health of ComfyUI, FFmpeg, TTS and storage plus hardware details and queue counts.</summary>
    [HttpGet("status")]
    public Task<SystemStatusDto> Status(CancellationToken ct) => status.GetAsync(ct);

    [HttpGet("info")]
    public Task<SystemInfoDto> Info(CancellationToken ct) => systemInfo.GetAsync(ct);
}

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    public SettingsDto Get() => settings.Get();

    [HttpPut]
    public Task<SettingsDto> Update(StudioSettings request, CancellationToken ct) => settings.UpdateAsync(request, ct);
}

[ApiController]
[Route("api/models")]
public sealed class ModelsController(ModelService models, IModelDownloadService downloads) : ControllerBase
{
    /// <summary>Model catalog with install status (as reported by ComfyUI) and a hardware readiness rating.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ModelDto>> List([FromQuery] GenerationType? capability, CancellationToken ct) =>
        models.ListAsync(capability, ct);

    [HttpGet("{id}")]
    public Task<ModelDto> Get(string id, CancellationToken ct) => models.GetAsync(id, ct);

    /// <summary>Exact file sizes (read from the server), target paths and free disk space. Downloads nothing.</summary>
    [HttpGet("{id}/download-plan")]
    public Task<ModelDownloadPlanDto> Plan(string id, CancellationToken ct) => downloads.GetPlanAsync(id, ct);

    /// <summary>Starts a download. Requires { "confirm": true } after the user has seen the plan.</summary>
    [HttpPost("{id}/download")]
    public async Task<ActionResult<ModelDownloadStatusDto>> Download(string id, ModelDownloadRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
        {
            throw new ValidationException("confirm", "Model downloads are large. Review the download plan and confirm explicitly (confirm: true).");
        }

        return Accepted(await downloads.StartAsync(id, ct));
    }

    [HttpGet("{id}/download")]
    public ActionResult<ModelDownloadStatusDto> DownloadStatus(string id) =>
        downloads.GetStatus(id) is { } status ? status : NotFound();

    [HttpPost("{id}/download/cancel")]
    public IActionResult CancelDownload(string id) => downloads.Cancel(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/prompts")]
public sealed class PromptsController(PromptService prompts) : ControllerBase
{
    /// <summary>Expands a short prompt. Uses the free rule-based enhancer unless another is configured.</summary>
    [HttpPost("enhance")]
    public Task<PromptEnhancementDto> Enhance(EnhancePromptRequest request, CancellationToken ct) => prompts.EnhanceAsync(request, ct);

    [HttpGet("camera-movements")]
    public IReadOnlyCollection<string> CameraMovementOptions() => CameraMovements.All;

    [HttpGet("enhancers")]
    public IReadOnlyList<string> Enhancers() => prompts.Available;
}
