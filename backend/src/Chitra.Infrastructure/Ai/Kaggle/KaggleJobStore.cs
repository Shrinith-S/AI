using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Chitra.Application.Abstractions;

namespace Chitra.Infrastructure.Ai.Kaggle;

public enum KaggleJobState
{
    Pending,
    Submitted,
    Running,
    Completed,
    Failed,
    Discarded,
}

public sealed class KaggleModelFile
{
    public string Folder { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Url { get; set; }
}

public sealed class KaggleInputFile
{
    /// <summary>File name inside ComfyUI's input folder on the worker.</summary>
    public string Name { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}

public sealed class KaggleJob
{
    public string Key { get; set; } = string.Empty;
    public Guid GenerationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public KaggleJobState State { get; set; } = KaggleJobState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public JsonObject Workflow { get; set; } = [];
    public List<KaggleModelFile> Files { get; set; } = [];
    public List<KaggleInputFile> Inputs { get; set; } = [];
    public int TimeoutSeconds { get; set; }
    public string? RunId { get; set; }
    public int PushAttempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public List<string> Outputs { get; set; } = [];
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public sealed class KaggleRun
{
    public string RunId { get; set; } = string.Empty;
    public string Kernel { get; set; } = string.Empty;
    public int? Version { get; set; }
    public string? Url { get; set; }
    public DateTime PushedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RunningSince { get; set; }
    public string Status { get; set; } = "QUEUED";
    public List<string> JobKeys { get; set; } = [];
    public DateTime? LastPolledAt { get; set; }
    public int PollFailures { get; set; }
    public string? Message { get; set; }
    public bool Finished { get; set; }
}

/// <summary>
/// Durable record of Kaggle jobs and runs (storage/kaggle/state.json), so pending batches and running Kaggle sessions
/// survive an API restart and are never submitted twice.
/// </summary>
public sealed class KaggleJobStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly string _statePath;
    private State _state;

    private sealed class State
    {
        public Dictionary<string, KaggleJob> Jobs { get; set; } = [];
        public Dictionary<string, KaggleRun> Runs { get; set; } = [];
        public string? ActiveRunId { get; set; }
    }

    public KaggleJobStore(IMediaStorage storage)
    {
        Root = Path.Combine(storage.RootPath, "kaggle");
        Directory.CreateDirectory(Root);
        _statePath = Path.Combine(Root, "state.json");
        _state = File.Exists(_statePath)
            ? JsonSerializer.Deserialize<State>(File.ReadAllText(_statePath), Json) ?? new State()
            : new State();
    }

    /// <summary>storage/kaggle — holds state.json and per-run push/output folders.</summary>
    public string Root { get; }

    public string RunDirectory(string runId) => Path.Combine(Root, "runs", runId);

    public void Add(KaggleJob job) => Mutate(s => s.Jobs[job.Key] = job);

    public KaggleJob? Get(string key)
    {
        lock (_lock) return _state.Jobs.TryGetValue(key, out var job) ? Clone(job) : null;
    }

    public KaggleRun? GetRun(string runId)
    {
        lock (_lock) return _state.Runs.TryGetValue(runId, out var run) ? Clone(run) : null;
    }

    public KaggleRun? ActiveRun
    {
        get
        {
            lock (_lock) return _state.ActiveRunId is { } id && _state.Runs.TryGetValue(id, out var run) ? Clone(run) : null;
        }
    }

    public IReadOnlyList<KaggleJob> Pending
    {
        get
        {
            lock (_lock)
            {
                return _state.Jobs.Values.Where(j => j.State == KaggleJobState.Pending).OrderBy(j => j.CreatedAt).Select(Clone).ToList();
            }
        }
    }

    public void UpdateJob(string key, Action<KaggleJob> update) => Mutate(s =>
    {
        if (s.Jobs.TryGetValue(key, out var job)) update(job);
    });

    public void UpdateRun(string runId, Action<KaggleRun> update) => Mutate(s =>
    {
        if (s.Runs.TryGetValue(runId, out var run)) update(run);
    });

    /// <summary>Records a pushed run and links its jobs to it in one atomic step.</summary>
    public void StartRun(KaggleRun run) => Mutate(s =>
    {
        s.Runs[run.RunId] = run;
        s.ActiveRunId = run.RunId;
        foreach (var key in run.JobKeys)
        {
            if (!s.Jobs.TryGetValue(key, out var job)) continue;
            job.RunId = run.RunId;
            if (job.State == KaggleJobState.Pending) job.State = KaggleJobState.Submitted;
        }
    });

    public void FinishRun(string runId) => Mutate(s =>
    {
        if (s.Runs.TryGetValue(runId, out var run)) run.Finished = true;
        if (s.ActiveRunId == runId) s.ActiveRunId = null;
    });

    /// <summary>Removes finished jobs and run folders older than <paramref name="age"/>; their outputs were already imported.</summary>
    public void Prune(TimeSpan age)
    {
        var cutoff = DateTime.UtcNow - age;
        List<string> oldRuns = [];
        Mutate(s =>
        {
            foreach (var key in s.Jobs.Values.Where(j => j.State is KaggleJobState.Completed or KaggleJobState.Failed or KaggleJobState.Discarded
                                                         && (j.FinishedAt ?? j.CreatedAt) < cutoff).Select(j => j.Key).ToList())
            {
                s.Jobs.Remove(key);
            }

            oldRuns = s.Runs.Values.Where(r => r.Finished && r.PushedAt < cutoff).Select(r => r.RunId).ToList();
            foreach (var id in oldRuns) s.Runs.Remove(id);
        });

        foreach (var id in oldRuns)
        {
            try
            {
                Directory.Delete(RunDirectory(id), recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private void Mutate(Action<State> change)
    {
        lock (_lock)
        {
            change(_state);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_state, Json));
            File.Move(tmp, _statePath, overwrite: true);
        }
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}
