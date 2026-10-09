using System.Collections.Concurrent;
using System.Threading.Channels;
using Chitra.Application.Abstractions;

namespace Chitra.Infrastructure.Background;

public sealed class GenerationQueue : IGenerationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public CancellationTokenSource RegisterRunning(Guid generationId, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[generationId] = cts;
        return cts;
    }

    public void UnregisterRunning(Guid generationId)
    {
        if (_running.TryRemove(generationId, out var cts)) cts.Dispose();
    }

    public bool TryCancelRunning(Guid generationId)
    {
        if (!_running.TryGetValue(generationId, out var cts)) return false;
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}

public sealed class ExportQueue : IExportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public ValueTask EnqueueAsync(Guid exportId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(exportId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public CancellationTokenSource RegisterRunning(Guid exportId, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[exportId] = cts;
        return cts;
    }

    public void UnregisterRunning(Guid exportId)
    {
        if (_running.TryRemove(exportId, out var cts)) cts.Dispose();
    }

    public bool TryCancelRunning(Guid exportId)
    {
        if (!_running.TryGetValue(exportId, out var cts)) return false;
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
