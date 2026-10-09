using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Settings;
using Chitra.Infrastructure.Ai.ComfyUi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Ai;

/// <summary>
/// Streams model files from their download URL into <c>{ComfyUI models}/{folder}/{file}</c>, resuming partial files,
/// and reports progress over SignalR. Nothing is downloaded automatically.
/// </summary>
public sealed class ModelDownloadService(
    HttpClient http,
    IModelCatalog catalog,
    IRuntimeSettings settings,
    IRealtimeNotifier notifier,
    IHostApplicationLifetime lifetime,
    ILogger<ModelDownloadService> logger) : IModelDownloadService
{
    private static readonly ConcurrentDictionary<string, DownloadState> Downloads = new(StringComparer.OrdinalIgnoreCase);

    private sealed class DownloadState
    {
        public required CancellationTokenSource Cancellation { get; init; }
        public required ModelDownloadStatusDto Status { get; set; }
    }

    public async Task<ModelDownloadPlanDto> GetPlanAsync(string modelId, CancellationToken ct)
    {
        var model = catalog.Find(modelId) ?? throw new NotFoundException("Model", modelId);
        var modelsPath = settings.Current.ComfyUiModelsPath;
        var pathOk = !string.IsNullOrWhiteSpace(modelsPath) && Directory.Exists(modelsPath);

        var files = new List<ModelDownloadFilePlanDto>();
        foreach (var file in model.Files)
        {
            var target = pathOk ? Path.Combine(modelsPath!, file.Folder, file.FileName) : null;
            var present = target is not null && File.Exists(target);
            var (size, exact) = present ? (new FileInfo(target!).Length, true) : await GetRemoteSizeAsync(file, ct);
            files.Add(new ModelDownloadFilePlanDto(file.FileName, file.Folder, file.DownloadUrl, size, exact, present, target));
        }

        var toDownload = files.Where(f => !f.AlreadyPresent).Sum(f => f.SizeBytes);
        long free = 0;
        if (pathOk)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(modelsPath!))!);
            free = drive.IsReady ? drive.AvailableFreeSpace : 0;
        }

        string? reason = null;
        if (!pathOk)
        {
            reason = string.IsNullOrWhiteSpace(modelsPath)
                ? "Set the ComfyUI models folder in Settings (e.g. C:\\ComfyUI_windows_portable\\ComfyUI\\models) to download from here."
                : $"The ComfyUI models folder '{modelsPath}' does not exist.";
        }
        else if (files.Any(f => !f.AlreadyPresent && f.Url is null))
        {
            reason = "Some files have no download URL; copy them into the ComfyUI models folder manually.";
        }
        else if (toDownload > 0 && free < toDownload + 2L * 1024 * 1024 * 1024)
        {
            reason = $"Not enough free disk space: {toDownload / 1024 / 1024 / 1024.0:0.#} GB needed plus 2 GB headroom, {free / 1024 / 1024 / 1024.0:0.#} GB free.";
        }
        else if (toDownload == 0)
        {
            reason = "All files are already present.";
        }

        return new ModelDownloadPlanDto(model.Id, model.Name, pathOk ? modelsPath : null, reason is null, reason, toDownload, free, files);
    }

    public async Task<ModelDownloadStatusDto> StartAsync(string modelId, CancellationToken ct)
    {
        if (Downloads.TryGetValue(modelId, out var existing) && existing.Status.State is "Pending" or "Downloading")
        {
            return existing.Status;
        }

        var plan = await GetPlanAsync(modelId, ct);
        if (!plan.CanDownload) throw new AppException(ErrorCode.Validation, plan.Reason ?? "This model cannot be downloaded.");

        var state = new DownloadState
        {
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping),
            Status = new ModelDownloadStatusDto(plan.ModelId, "Pending", null, 0, plan.TotalBytesToDownload, 0, null),
        };
        Downloads[modelId] = state;
        _ = Task.Run(() => RunAsync(plan, state), CancellationToken.None);
        return state.Status;
    }

    public bool Cancel(string modelId)
    {
        if (!Downloads.TryGetValue(modelId, out var state) || state.Status.State is not ("Pending" or "Downloading")) return false;
        state.Cancellation.Cancel();
        return true;
    }

    public ModelDownloadStatusDto? GetStatus(string modelId) => Downloads.TryGetValue(modelId, out var s) ? s.Status : null;

    private async Task RunAsync(ModelDownloadPlanDto plan, DownloadState state)
    {
        var ct = state.Cancellation.Token;
        long done = 0;
        var lastNotify = DateTime.MinValue;
        try
        {
            foreach (var file in plan.Files.Where(f => !f.AlreadyPresent))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath!)!);
                var partial = file.TargetPath + ".part";
                var resumeFrom = File.Exists(partial) ? new FileInfo(partial).Length : 0;

                using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
                if (resumeFrom > 0) request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new AppException(ErrorCode.Validation,
                        $"{file.FileName} requires accepting its license (gated download). Download it in your browser and copy it to models/{file.Folder}.");
                }

                response.EnsureSuccessStatusCode();
                var append = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!append) resumeFrom = 0;
                done += resumeFrom;

                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
                {
                    var buffer = new byte[1 << 20];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        if (DateTime.UtcNow - lastNotify > TimeSpan.FromMilliseconds(750))
                        {
                            lastNotify = DateTime.UtcNow;
                            await UpdateAsync(state, "Downloading", file.FileName, done, plan.TotalBytesToDownload, null);
                        }
                    }
                }

                File.Move(partial, file.TargetPath!, overwrite: true);
                logger.LogInformation("Downloaded {File} to {Path}", file.FileName, file.TargetPath);
            }

            ComfyUiProvider.InvalidateCaches();
            await UpdateAsync(state, "Completed", null, done, plan.TotalBytesToDownload, null);
        }
        catch (OperationCanceledException)
        {
            // Partial files are kept so the download can resume later.
            await UpdateAsync(state, "Cancelled", null, done, plan.TotalBytesToDownload, "Download cancelled. Starting again resumes where it stopped.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model download for {Model} failed", plan.ModelId);
            var message = ex is AppException ? ex.Message : $"Download failed: {ex.Message}. Starting again resumes where it stopped.";
            await UpdateAsync(state, "Failed", null, done, plan.TotalBytesToDownload, message);
        }
    }

    private async Task UpdateAsync(DownloadState state, string phase, string? file, long done, long total, string? error)
    {
        var progress = total > 0 ? (int)Math.Clamp(done * 100 / total, 0, 100) : phase == "Completed" ? 100 : 0;
        state.Status = state.Status with { State = phase, CurrentFile = file, BytesDownloaded = done, TotalBytes = total, Progress = progress, Error = error };
        try
        {
            await notifier.ModelDownloadUpdatedAsync(state.Status);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not push download progress");
        }
    }

    private async Task<(long Size, bool Exact)> GetRemoteSizeAsync(ModelFileDefinition file, CancellationToken ct)
    {
        if (file.DownloadUrl is null) return (file.SizeBytes, false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, file.DownloadUrl), cts.Token);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is > 0 and var length)
            {
                return (length, true);
            }

            if (response.Headers.TryGetValues("X-Linked-Size", out var linked) && long.TryParse(linked.FirstOrDefault(), out var linkedSize))
            {
                return (linkedSize, true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Could not read size of {Url}", file.DownloadUrl);
        }

        return (file.SizeBytes, false);
    }
}
