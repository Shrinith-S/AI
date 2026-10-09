using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Chitra.Application.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Ai.ComfyUi;

public sealed class PromptProgress
{
    public bool Started { get; set; }
    public bool Finished { get; set; }
    public bool Failed { get; set; }
    public bool Interrupted { get; set; }
    public string? CurrentNode { get; set; }
    public string? CurrentNodeType { get; set; }
    public int StepValue { get; set; }
    public int StepMax { get; set; }
    public int TotalNodes { get; set; }
    public HashSet<string> CompletedNodes { get; } = [];
    public Dictionary<string, string> NodeTypes { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Overall percentage. Sampling dominates run time, so sampler steps drive most of the bar; node completion
    /// fills in loading and decoding phases.
    /// </summary>
    public int Percent
    {
        get
        {
            if (Finished) return 100;
            var nodeFraction = TotalNodes > 0 ? CompletedNodes.Count / (double)TotalNodes : 0;
            var stepFraction = StepMax > 0 ? StepValue / (double)StepMax : 0;
            var value = StepMax > 0 ? 0.1 * nodeFraction + 0.85 * stepFraction : 0.3 * nodeFraction;
            return (int)Math.Clamp(Math.Round(value * 100), 0, 99);
        }
    }

    public string Message =>
        StepMax > 0 && StepValue < StepMax
            ? $"Sampling step {StepValue}/{StepMax}"
            : StepMax > 0 && StepValue >= StepMax
                ? "Decoding and saving"
                : CurrentNodeType?.Contains("Loader", StringComparison.OrdinalIgnoreCase) == true || CurrentNodeType is null
                    ? "Loading models"
                    : $"Running {CurrentNodeType}";
}

/// <summary>
/// Listens on ComfyUI's WebSocket for step-level progress of prompts submitted with <see cref="ClientId"/>.
/// Completion is still confirmed through /history, so a dropped connection only degrades progress detail.
/// </summary>
public sealed class ComfyUiProgressTracker : BackgroundService
{
    private readonly IRuntimeSettings _settings;
    private readonly ILogger<ComfyUiProgressTracker> _logger;
    private readonly ConcurrentDictionary<string, PromptProgress> _prompts = new();
    private CancellationTokenSource? _connectionCts;

    public ComfyUiProgressTracker(IRuntimeSettings settings, ILogger<ComfyUiProgressTracker> logger)
    {
        _settings = settings;
        _logger = logger;
        _settings.Changed += _ => _connectionCts?.Cancel();
    }

    public string ClientId { get; } = $"chitra-{Guid.NewGuid():N}";

    public bool IsConnected { get; private set; }

    public void Register(string promptId, IReadOnlyDictionary<string, string> nodeTypes) =>
        _prompts[promptId] = new PromptProgress { TotalNodes = nodeTypes.Count, NodeTypes = new Dictionary<string, string>(nodeTypes) };

    public PromptProgress? Get(string promptId) => _prompts.GetValueOrDefault(promptId);

    public void Forget(string promptId) => _prompts.TryRemove(promptId, out _);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                await ListenAsync(_connectionCts.Token);
                delay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ComfyUI WebSocket unavailable; retrying in {Delay}", delay);
                delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
            }
            finally
            {
                IsConnected = false;
                _connectionCts.Dispose();
                _connectionCts = null;
            }

            PruneOld();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var baseUri = new Uri(_settings.Current.ComfyUiUrl.TrimEnd('/') + "/");
        var builder = new UriBuilder(new Uri(baseUri, "ws"))
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Query = $"clientId={ClientId}",
        };

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(builder.Uri, ct);
        IsConnected = true;
        _logger.LogInformation("Connected to ComfyUI progress stream at {Uri}", builder.Uri);

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) break;

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                // Latent previews; drain and ignore.
                if (result.EndOfMessage) message.SetLength(0);
                continue;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            Handle(text);
        }
    }

    internal void Handle(string text)
    {
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        var type = json?["type"]?.GetValue<string>();
        var data = json?["data"] as JsonObject;
        var promptId = data?["prompt_id"]?.GetValue<string>();
        if (type is null || promptId is null || !_prompts.TryGetValue(promptId, out var p)) return;

        lock (p)
        {
            p.UpdatedAt = DateTime.UtcNow;
            switch (type)
            {
                case "execution_start":
                    p.Started = true;
                    break;
                case "execution_cached":
                    p.Started = true;
                    foreach (var node in data!["nodes"]?.AsArray() ?? [])
                    {
                        if (node?.GetValue<string>() is { } id) p.CompletedNodes.Add(id);
                    }

                    break;
                case "executing":
                    p.Started = true;
                    var nodeId = data!["node"]?.GetValue<string>();
                    if (p.CurrentNode is not null) p.CompletedNodes.Add(p.CurrentNode);
                    if (nodeId is null)
                    {
                        p.Finished = true;
                    }
                    else
                    {
                        p.CurrentNode = nodeId;
                        p.CurrentNodeType = p.NodeTypes.GetValueOrDefault(nodeId);
                    }

                    break;
                case "progress":
                    p.Started = true;
                    p.StepValue = data!["value"]?.GetValue<int>() ?? 0;
                    p.StepMax = data["max"]?.GetValue<int>() ?? 0;
                    break;
                case "executed":
                    if (data!["node"]?.GetValue<string>() is { } done) p.CompletedNodes.Add(done);
                    break;
                case "execution_success":
                    p.Finished = true;
                    break;
                case "execution_error":
                    p.Failed = true;
                    p.Finished = true;
                    break;
                case "execution_interrupted":
                    p.Interrupted = true;
                    p.Finished = true;
                    break;
            }
        }
    }

    private void PruneOld()
    {
        var cutoff = DateTime.UtcNow.AddHours(-6);
        foreach (var (id, progress) in _prompts)
        {
            if (progress.UpdatedAt < cutoff) _prompts.TryRemove(id, out _);
        }
    }
}
