using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chitra.Application.Common;
using Chitra.Application.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Ai.Kaggle;

/// <summary>Resolves (and caches) the Kaggle account so health checks and pushes do not call the API every second.</summary>
public sealed class KaggleAccount(IKaggleApi api, IRuntimeSettings settings)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTime At, string Python, KaggleResult Result)? _cache;

    public async Task<(string? Username, string? Error, bool AuthError)> GetAsync(CancellationToken ct)
    {
        var current = settings.Current;
        await _lock.WaitAsync(ct);
        try
        {
            var fresh = _cache is { } c && c.Python == current.KagglePythonPath &&
                        DateTime.UtcNow - c.At < (c.Result.Ok ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(30));
            if (!fresh)
            {
                _cache = (DateTime.UtcNow, current.KagglePythonPath, await api.WhoAmIAsync(ct));
            }

            var result = _cache!.Value.Result;
            if (!result.Ok) return (null, result.Error, result.AuthError);
            var username = string.IsNullOrWhiteSpace(current.KaggleUsername) ? result.Data["username"]?.GetValue<string>() : current.KaggleUsername.Trim();
            return (username, username is null ? "Could not determine the Kaggle username; set it in Settings." : null, false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _cache = null;
}

/// <summary>
/// Batches queued Kaggle jobs into notebook runs, pushes them with the official API, polls their status and
/// imports the outputs. One run is active at a time (a Kaggle notebook runs one session at a time); both T4 GPUs are
/// used inside that run by the worker.
/// </summary>
public sealed class KaggleRunCoordinator(
    KaggleJobStore store,
    IKaggleApi api,
    KaggleAccount account,
    IRuntimeSettings settings,
    ILogger<KaggleRunCoordinator> logger) : BackgroundService
{
    /// <summary>Embedded payload budget per run; larger batches are split. Kept well under any upload limit.</summary>
    internal const int MaxPayloadChars = 4_000_000;

    internal static TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(5);
    internal static TimeSpan StatusPollInterval { get; set; } = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>Wakes the coordinator, e.g. when a job was queued.</summary>
    public void Signal()
    {
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPrune = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                if (DateTime.UtcNow - lastPrune > TimeSpan.FromHours(6))
                {
                    store.Prune(TimeSpan.FromDays(3));
                    lastPrune = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kaggle coordinator tick failed");
            }

            try
            {
                await _signal.WaitAsync(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        if (store.ActiveRun is { } run)
        {
            await PollAsync(run, ct);
        }
        else
        {
            await MaybePushAsync(ct);
        }
    }

    private async Task MaybePushAsync(CancellationToken ct)
    {
        var current = settings.Current;
        var pending = store.Pending.Where(j => j.NextAttemptAt is null || j.NextAttemptAt <= DateTime.UtcNow).ToList();
        if (pending.Count == 0) return;

        var oldestAge = DateTime.UtcNow - pending[0].CreatedAt;
        if (oldestAge < TimeSpan.FromSeconds(current.KaggleBatchWindowSeconds) && pending.Count < current.KaggleMaxJobsPerRun) return;

        var (username, accountError, authError) = await account.GetAsync(ct);
        if (username is null)
        {
            FailJobs(pending.Select(j => j.Key), ErrorCode.ProviderUnavailable, accountError ?? "Kaggle is not configured.");
            if (authError) account.Invalidate();
            return;
        }

        // Fill the batch up to the job limit and payload budget (the first job always goes).
        var batch = new List<(KaggleJob Job, JsonObject Payload)>();
        var size = 0;
        foreach (var job in pending.Take(current.KaggleMaxJobsPerRun))
        {
            JsonObject payload;
            try
            {
                payload = BuildJobPayload(job);
            }
            catch (IOException ex)
            {
                FailJobs([job.Key], ErrorCode.Validation, $"Could not read the input image: {ex.Message}");
                continue;
            }

            var jobSize = payload.ToJsonString().Length;
            if (batch.Count > 0 && size + jobSize > MaxPayloadChars) break;
            batch.Add((job, payload));
            size += jobSize;
        }

        if (batch.Count == 0) return;

        var runId = $"r{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var kernel = $"{username}/{current.KaggleKernelSlug}";
        var pushDir = Path.Combine(store.RunDirectory(runId), "push");
        Directory.CreateDirectory(pushDir);

        var manifest = new JsonObject { ["runId"] = runId, ["jobs"] = new JsonArray(batch.Select(b => (JsonNode)b.Payload).ToArray()) };
        await File.WriteAllTextAsync(Path.Combine(pushDir, "run.py"), RenderWorker(manifest), ct);
        await File.WriteAllTextAsync(Path.Combine(pushDir, "kernel-metadata.json"), new JsonObject
        {
            ["id"] = kernel,
            ["title"] = current.KaggleKernelSlug,
            ["code_file"] = "run.py",
            ["language"] = "python",
            ["kernel_type"] = "script",
            ["is_private"] = true,
            ["enable_gpu"] = true,
            ["enable_internet"] = true,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);

        var timeoutSeconds = current.KaggleRunTimeoutMinutes * 60;
        logger.LogInformation("Pushing Kaggle run {RunId} with {Count} job(s) to {Kernel}", runId, batch.Count, kernel);
        var result = await api.PushAsync(pushDir, timeoutSeconds, current.KaggleAccelerator, ct);

        if (!result.Ok)
        {
            var error = result.Error ?? "Kaggle rejected the notebook push.";
            if (IsQuotaError(error))
            {
                FailJobs(batch.Select(b => b.Job.Key), ErrorCode.ProviderUnavailable,
                    $"Kaggle GPU quota is exhausted or no GPU is available ({error}). The free quota resets weekly; switch to local ComfyUI meanwhile.");
            }
            else if (result.AuthError)
            {
                account.Invalidate();
                FailJobs(batch.Select(b => b.Job.Key), ErrorCode.ProviderUnavailable, error);
            }
            else
            {
                // Nothing ran, so retrying the push cannot duplicate work.
                foreach (var (job, _) in batch)
                {
                    if (job.PushAttempts + 1 >= 3)
                    {
                        FailJobs([job.Key], ErrorCode.ProviderUnavailable, $"Could not start a Kaggle run after 3 attempts: {error}");
                    }
                    else
                    {
                        store.UpdateJob(job.Key, j =>
                        {
                            j.PushAttempts++;
                            j.NextAttemptAt = DateTime.UtcNow.AddMinutes(1);
                            j.Error = error;
                        });
                    }
                }
            }

            logger.LogWarning("Kaggle push failed: {Error}", error);
            return;
        }

        store.StartRun(new KaggleRun
        {
            RunId = runId,
            Kernel = kernel,
            Version = result.Data["version"]?.GetValue<int>(),
            Url = result.Data["url"]?.GetValue<string>() ?? $"https://www.kaggle.com/code/{kernel}",
            JobKeys = batch.Select(b => b.Job.Key).ToList(),
            Status = "QUEUED",
        });
    }

    private async Task PollAsync(KaggleRun run, CancellationToken ct)
    {
        if (run.LastPolledAt is { } last && DateTime.UtcNow - last < StatusPollInterval) return;
        var current = settings.Current;
        var result = await api.StatusAsync(run.Kernel, ct);
        store.UpdateRun(run.RunId, r => r.LastPolledAt = DateTime.UtcNow);

        if (!result.Ok)
        {
            store.UpdateRun(run.RunId, r =>
            {
                r.PollFailures++;
                r.Message = result.Error;
            });
            logger.LogWarning("Kaggle status check failed ({Failures}): {Error}", run.PollFailures + 1, result.Error);
            if (DateTime.UtcNow - run.PushedAt > TimeSpan.FromMinutes(current.KaggleRunTimeoutMinutes + 60))
            {
                await FinishWithErrorAsync(run, ErrorCode.ProviderUnavailable,
                    $"Lost contact with the Kaggle run ({result.Error}). Check it at {run.Url}.");
            }

            return;
        }

        var status = result.Data["status"]?.GetValue<string>() ?? "UNKNOWN";
        var failure = result.Data["failureMessage"]?.GetValue<string>();
        store.UpdateRun(run.RunId, r =>
        {
            r.Status = status;
            r.PollFailures = 0;
            r.Message = failure;
            if (status == "RUNNING") r.RunningSince ??= DateTime.UtcNow;
        });

        switch (status)
        {
            case "RUNNING":
                foreach (var key in run.JobKeys)
                {
                    store.UpdateJob(key, j =>
                    {
                        if (j.State == KaggleJobState.Submitted) j.State = KaggleJobState.Running;
                    });
                }

                break;

            case "COMPLETE":
            case "ERROR":
                await CollectAsync(run, status == "COMPLETE", failure, ct);
                break;

            case "CANCEL_REQUESTED":
            case "CANCEL_ACKNOWLEDGED":
                await FinishWithErrorAsync(run, ErrorCode.Cancelled, $"The Kaggle run was cancelled ({run.Url}).");
                break;
        }

        // A run that never ends (stuck in the Kaggle queue) must not block every later job.
        if (status is "QUEUED" or "NEW_SCRIPT" or "RUNNING" &&
            DateTime.UtcNow - run.PushedAt > TimeSpan.FromMinutes(current.KaggleRunTimeoutMinutes + 60))
        {
            await FinishWithErrorAsync(run, ErrorCode.Timeout,
                $"The Kaggle run did not finish within {current.KaggleRunTimeoutMinutes + 60} minutes ({run.Url}).");
        }
    }

    private async Task CollectAsync(KaggleRun run, bool succeeded, string? failure, CancellationToken ct)
    {
        var outputDir = Path.Combine(store.RunDirectory(run.RunId), "output");
        Directory.CreateDirectory(outputDir);
        var kernelVersion = run.Version is { } v ? $"{run.Kernel}/{v}" : run.Kernel;
        var download = await api.DownloadOutputAsync(kernelVersion, outputDir, ct);

        var resultsPath = Directory.EnumerateFiles(outputDir, "results.json", SearchOption.AllDirectories).FirstOrDefault();
        if (resultsPath is null)
        {
            var reason = failure ?? download.Error;
            var logTail = (await api.LogsAsync(run.Kernel, ct)).Data["log"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(logTail))
            {
                await File.WriteAllTextAsync(Path.Combine(outputDir, "kaggle-log.txt"), logTail, ct);
            }

            var code = reason is not null && IsQuotaError(reason) ? ErrorCode.ProviderUnavailable : ErrorCode.GenerationFailed;
            await FinishWithErrorAsync(run, code, succeeded
                ? $"The Kaggle run finished but produced no results.json. See the log at {run.Url}."
                : $"The Kaggle run failed{(reason is null ? string.Empty : $": {reason}")}. See the log at {run.Url}.");
            return;
        }

        var outputsRoot = Path.GetDirectoryName(resultsPath)!;
        var results = JsonNode.Parse(await File.ReadAllTextAsync(resultsPath, ct)) as JsonObject ?? [];
        if (results["runId"]?.GetValue<string>() != run.RunId)
        {
            await FinishWithErrorAsync(run, ErrorCode.GenerationFailed,
                $"Downloaded Kaggle output belongs to a different run ({results["runId"]}). Was the notebook run manually? See {run.Url}.");
            return;
        }

        var runError = results["error"]?.GetValue<string>();
        foreach (var key in run.JobKeys)
        {
            var entry = results["jobs"]?[key] as JsonObject;
            var state = entry?["status"]?.GetValue<string>();
            if (state == "completed")
            {
                var outputs = (entry!["outputs"] as JsonArray ?? [])
                    .Select(o => o?.GetValue<string>())
                    .OfType<string>()
                    .Select(rel => Path.GetFullPath(Path.Combine(outputsRoot, rel)))
                    .Where(p => p.StartsWith(outputsRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                    .ToList();

                store.UpdateJob(key, j =>
                {
                    if (j.State == KaggleJobState.Discarded) return;
                    j.State = outputs.Count > 0 ? KaggleJobState.Completed : KaggleJobState.Failed;
                    j.Outputs = outputs;
                    j.Error = outputs.Count > 0 ? null : "The Kaggle worker reported outputs that were not in the downloaded files.";
                    j.ErrorCode = outputs.Count > 0 ? null : nameof(ErrorCode.GenerationFailed);
                    j.FinishedAt = DateTime.UtcNow;
                });
            }
            else
            {
                var error = entry?["error"]?.GetValue<string>()
                            ?? (state is "pending" or "running"
                                ? $"The Kaggle session ended before this job finished{((failure ?? runError) is { } r ? $" ({r})" : string.Empty)}."
                                : runError ?? "The Kaggle worker did not report this job.");
                FailJobs([key], ParseCode(entry?["errorCode"]?.GetValue<string>()), error);
            }
        }

        store.FinishRun(run.RunId);
        logger.LogInformation("Kaggle run {RunId} collected ({Succeeded})", run.RunId, succeeded ? "complete" : "error");
    }

    private async Task FinishWithErrorAsync(KaggleRun run, ErrorCode code, string message)
    {
        FailJobs(run.JobKeys, code, message);
        store.FinishRun(run.RunId);
        logger.LogWarning("Kaggle run {RunId} failed: {Message}", run.RunId, message);
        await Task.CompletedTask;
    }

    private void FailJobs(IEnumerable<string> keys, ErrorCode code, string message)
    {
        foreach (var key in keys)
        {
            store.UpdateJob(key, j =>
            {
                if (j.State is KaggleJobState.Completed or KaggleJobState.Discarded) return;
                j.State = KaggleJobState.Failed;
                j.Error = message;
                j.ErrorCode = code.ToString();
                j.FinishedAt = DateTime.UtcNow;
            });
        }
    }

    internal static JsonObject BuildJobPayload(KaggleJob job) => new()
    {
        ["key"] = job.Key,
        ["type"] = job.Type,
        ["modelId"] = job.ModelId,
        ["timeoutSeconds"] = job.TimeoutSeconds,
        ["workflow"] = job.Workflow.DeepClone(),
        ["files"] = new JsonArray(job.Files.Select(f => (JsonNode)new JsonObject
        {
            ["folder"] = f.Folder,
            ["fileName"] = f.FileName,
            ["sizeBytes"] = f.SizeBytes,
            ["url"] = f.Url,
        }).ToArray()),
        ["inputs"] = new JsonArray(job.Inputs.Select(i => (JsonNode)new JsonObject
        {
            ["name"] = i.Name,
            ["dataB64"] = Convert.ToBase64String(File.ReadAllBytes(i.LocalPath)),
        }).ToArray()),
    };

    /// <summary>The version-controlled worker template with this run's manifest embedded (gzip + base64).</summary>
    internal static string RenderWorker(JsonObject manifest)
    {
        var template = File.ReadAllText(Path.Combine(KaggleBridgeApi.ScriptsDirectory, "worker.py"));
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }

        const string marker = "PAYLOAD = \"__CHITRA_PAYLOAD__\"";
        if (!template.Contains(marker)) throw new InvalidOperationException("worker.py is missing its payload marker.");
        return template.Replace(marker, $"PAYLOAD = \"{Convert.ToBase64String(buffer.ToArray())}\"");
    }

    internal static bool IsQuotaError(string message) =>
        message.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("accelerator", StringComparison.OrdinalIgnoreCase) && message.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("No CUDA GPU", StringComparison.OrdinalIgnoreCase);

    private static ErrorCode ParseCode(string? code) =>
        Enum.TryParse<ErrorCode>(code, out var parsed) ? parsed : ErrorCode.GenerationFailed;
}
