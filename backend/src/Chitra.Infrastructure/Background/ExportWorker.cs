using Chitra.Application.Abstractions;
using Chitra.Application.Exports;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Background;

/// <summary>Runs FFmpeg exports one at a time (they are CPU/disk heavy). Unfinished exports restart after a reboot.</summary>
public sealed class ExportWorker(IServiceScopeFactory scopes, IExportQueue queue, ILogger<ExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        await foreach (var exportId in queue.DequeueAllAsync(stoppingToken))
        {
            using var cts = queue.RegisterRunning(exportId, stoppingToken);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ExportProcessor>().ProcessAsync(exportId, cts.Token, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Export {ExportId} crashed", exportId);
            }
            finally
            {
                queue.UnregisterRunning(exportId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var pending = await db.VideoExports
                .Where(e => e.Status == GenerationStatus.Queued || e.Status == GenerationStatus.Processing)
                .OrderBy(e => e.CreatedAt).Select(e => e.Id).ToListAsync(ct);
            foreach (var id in pending) await queue.EnqueueAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not recover unfinished exports");
        }
    }
}
