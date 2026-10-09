using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Chitra.Infrastructure.Ai.Kaggle;
using Chitra.Infrastructure.Processes;

namespace Chitra.Tests.Integration;

public enum FakeKaggleMode
{
    Success,
    QuotaOnPush,
    OutOfMemory,
    SessionKilled,
}

/// <summary>
/// Test double for the Kaggle API. It decodes the real run.py Chitra pushes and simulates what the worker would
/// write to /kaggle/working. Optionally (<see cref="LocalComfyUiDir"/>) it executes that run.py for real with the
/// Python worker against a local ComfyUI.
/// </summary>
public sealed class FakeKaggleApi : IKaggleApi
{
    private int _version;
    private Task? _localRun;

    public FakeKaggleMode Mode { get; set; } = FakeKaggleMode.Success;
    public List<JsonObject> Manifests { get; } = [];
    public List<string> PushedFolders { get; } = [];
    public List<string> PushedFileLists { get; } = [];
    public int StatusCalls { get; private set; }

    /// <summary>When set, run.py is executed with the real worker using this ComfyUI checkout (CPU).</summary>
    public string? LocalComfyUiDir { get; set; }
    public string? LocalComfyUiPython { get; set; }
    public string? LocalWorkerLog { get; private set; }

    public Task<KaggleResult> WhoAmIAsync(CancellationToken ct) => Ok(new JsonObject { ["username"] = "testuser" });

    public Task<KaggleResult> PushAsync(string folder, int timeoutSeconds, string accelerator, CancellationToken ct)
    {
        if (Mode == FakeKaggleMode.QuotaOnPush)
        {
            return Task.FromResult(new KaggleResult(false, "You have exceeded your weekly GPU quota.", false, []));
        }

        PushedFolders.Add(folder);
        PushedFileLists.Add(string.Join(",", Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n)));
        var script = File.ReadAllText(Path.Combine(folder, "run.py"));
        var encoded = Regex.Match(script, "PAYLOAD = \"([A-Za-z0-9+/=]+)\"").Groups[1].Value;
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        Manifests.Add(JsonNode.Parse(reader.ReadToEnd())!.AsObject());
        _version++;

        if (LocalComfyUiDir is not null)
        {
            _localRun = Task.Run(() => RunWorkerLocally(folder));
        }

        return Ok(new JsonObject { ["version"] = _version, ["url"] = $"https://www.kaggle.com/code/testuser/chitra-gpu-worker/{_version}" });
    }

    public Task<KaggleResult> StatusAsync(string kernel, CancellationToken ct)
    {
        StatusCalls++;
        if (_localRun is not null)
        {
            return Ok(new JsonObject { ["status"] = _localRun.IsCompleted ? "COMPLETE" : "RUNNING" });
        }

        var status = StatusCalls switch
        {
            1 => "QUEUED",
            2 => "RUNNING",
            _ => Mode == FakeKaggleMode.SessionKilled ? "ERROR" : "COMPLETE",
        };
        return Ok(new JsonObject
        {
            ["status"] = status,
            ["failureMessage"] = Mode == FakeKaggleMode.SessionKilled && status == "ERROR" ? "Session timed out" : null,
        });
    }

    public Task<KaggleResult> DownloadOutputAsync(string kernel, string destination, CancellationToken ct)
    {
        var outputs = Path.Combine(destination, "chitra_outputs");
        Directory.CreateDirectory(outputs);
        var manifest = Manifests[^1];

        if (LocalComfyUiDir is not null)
        {
            CopyDirectory(Path.Combine(PushedFolders[^1], "..", "fake-working", "chitra_outputs"), outputs);
            return Ok([]);
        }

        var jobs = new JsonObject();
        var first = true;
        foreach (var job in manifest["jobs"]!.AsArray())
        {
            var key = job!["key"]!.GetValue<string>();
            var isVideo = job["type"]!.GetValue<string>() != "TextToImage";
            if (Mode == FakeKaggleMode.OutOfMemory)
            {
                jobs[key] = new JsonObject { ["status"] = "failed", ["errorCode"] = "InsufficientVram", ["error"] = "The Kaggle GPU ran out of memory in KSampler." };
            }
            else if (Mode == FakeKaggleMode.SessionKilled && !first)
            {
                jobs[key] = new JsonObject { ["status"] = "running" };
            }
            else
            {
                var name = isVideo ? "clip_00001_.mp4" : "image_00001_.png";
                Directory.CreateDirectory(Path.Combine(outputs, key));
                File.WriteAllBytes(Path.Combine(outputs, key, name), VideoBytes ?? FakeComfyUi.PngBytes);
                jobs[key] = new JsonObject { ["status"] = "completed", ["outputs"] = new JsonArray($"{key}/{name}") };
            }

            first = false;
        }

        var results = new JsonObject { ["runId"] = manifest["runId"]!.GetValue<string>(), ["error"] = null, ["jobs"] = jobs };
        File.WriteAllText(Path.Combine(outputs, "results.json"), results.ToJsonString());
        return Ok([]);
    }

    /// <summary>Optional real video bytes (e.g. from FFmpeg) used for video outputs.</summary>
    public byte[]? VideoBytes { get; set; }

    public Task<KaggleResult> LogsAsync(string kernel, CancellationToken ct) => Ok(new JsonObject { ["log"] = "fake log" });

    private async Task RunWorkerLocally(string folder)
    {
        var working = Path.GetFullPath(Path.Combine(folder, "..", "fake-working"));
        var psi = new ProcessStartInfo(LocalComfyUiPython!, $"\"{Path.Combine(folder, "run.py")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["CHITRA_OUT"] = Path.Combine(working, "chitra_outputs");
        psi.Environment["CHITRA_WORK"] = Path.Combine(working, "work");
        psi.Environment["CHITRA_COMFYUI_DIR"] = LocalComfyUiDir;
        psi.Environment["CHITRA_COMFYUI_PYTHON"] = LocalComfyUiPython;
        psi.Environment["CHITRA_ALLOW_CPU"] = "1";
        psi.Environment["CHITRA_BASE_PORT"] = "8195";
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        LocalWorkerLog = await stdout + await stderr;
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static Task<KaggleResult> Ok(JsonObject data) => Task.FromResult(new KaggleResult(true, null, false, data));
}
