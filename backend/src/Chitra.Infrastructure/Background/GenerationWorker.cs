using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Generations;
using Chitra.Application.Settings;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Background;

/// <summary>
/// Executes queued generation jobs in the background with a configurable concurrency limit, so HTTP requests
/// never wait for inference. Unfinished jobs are re-queued (or resumed) after a restart.
/// </summary>
public sealed class GenerationWorker(
    IServiceScopeFactory scopes,
    IGenerationQueue queue,
    IRuntimeSettings settings,
    ILogger<GenerationWorker> logger) : BackgroundService
{
    private int _running;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        var tasks = new List<Task>();
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            // Batch providers (Kaggle) queue remotely; limiting them locally would stop jobs from sharing a run.
            var counted = !await ManagesOwnQueueAsync(jobId, stoppingToken);
            if (counted)
            {
                while (Volatile.Read(ref _running) >= Math.Max(1, settings.Current.MaxConcurrentGenerations))
                {
                    await Task.Delay(250, stoppingToken);
                }

                Interlocked.Increment(ref _running);
            }

            tasks.RemoveAll(t => t.IsCompleted);
            tasks.Add(Task.Run(() => RunJobAsync(jobId, counted, stoppingToken), CancellationToken.None));
        }

        await Task.WhenAll(tasks);
    }

    private async Task<bool> ManagesOwnQueueAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var providerName = await db.GenerationJobs.Where(j => j.Id == jobId).Select(j => j.ProviderName).FirstOrDefaultAsync(ct);
            var providers = scope.ServiceProvider.GetRequiredService<IAiProviderRegistry>().All;
            return providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase))?.ManagesOwnQueue == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not determine the provider of job {JobId}", jobId);
            return false;
        }
    }

    private async Task RunJobAsync(Guid jobId, bool counted, CancellationToken stoppingToken)
    {
        Guid? generationId = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            generationId = await db.GenerationJobs.Where(j => j.Id == jobId).Select(j => (Guid?)j.GenerationId).FirstOrDefaultAsync(stoppingToken);
            if (generationId is null) return;

            using var cts = queue.RegisterRunning(generationId.Value, stoppingToken);
            var processor = scope.ServiceProvider.GetRequiredService<GenerationProcessor>();
            await processor.ProcessAsync(jobId, cts.Token, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Generation job {JobId} crashed", jobId);
        }
        finally
        {
            if (generationId is not null) queue.UnregisterRunning(generationId.Value);
            if (counted) Interlocked.Decrement(ref _running);
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var pending = await db.GenerationJobs
                .Where(j => j.Status == GenerationStatus.Queued || j.Status == GenerationStatus.Processing)
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(ct);

            foreach (var id in pending) await queue.EnqueueAsync(id, ct);
            if (pending.Count > 0) logger.LogInformation("Re-queued {Count} unfinished generation job(s)", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not recover unfinished generation jobs");
        }
    }
}
