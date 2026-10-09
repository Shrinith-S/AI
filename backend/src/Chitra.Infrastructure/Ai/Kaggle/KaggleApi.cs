using System.Text.Json;
using System.Text.Json.Nodes;
using Chitra.Application.Settings;
using Chitra.Infrastructure.Processes;

namespace Chitra.Infrastructure.Ai.Kaggle;

public sealed record KaggleResult(bool Ok, string? Error, bool AuthError, JsonObject Data);

/// <summary>The subset of the official Kaggle API that Chitra uses. Faked in tests.</summary>
public interface IKaggleApi
{
    Task<KaggleResult> WhoAmIAsync(CancellationToken ct);

    /// <summary>Uploads a new version of the kernel in <paramref name="folder"/> and starts it on <paramref name="accelerator"/>.</summary>
    Task<KaggleResult> PushAsync(string folder, int timeoutSeconds, string accelerator, CancellationToken ct);

    /// <summary>Status of the latest session: QUEUED, RUNNING, COMPLETE, ERROR, CANCEL_REQUESTED, CANCEL_ACKNOWLEDGED, NEW_SCRIPT.</summary>
    Task<KaggleResult> StatusAsync(string kernel, CancellationToken ct);

    /// <summary>Downloads a kernel version's /kaggle/working output into <paramref name="destination"/>.</summary>
    Task<KaggleResult> DownloadOutputAsync(string kernel, string destination, CancellationToken ct);

    Task<KaggleResult> LogsAsync(string kernel, CancellationToken ct);
}

/// <summary>
/// Calls the official Kaggle Python client through kaggle/bridge.py, which prints one JSON object per call.
/// Credentials stay where the Kaggle client keeps them (~/.kaggle or KAGGLE_API_TOKEN) and never pass through Chitra.
/// </summary>
public sealed class KaggleBridgeApi(IRuntimeSettings settings) : IKaggleApi
{
    public static string ScriptsDirectory => Path.Combine(AppContext.BaseDirectory, "kaggle");

    public Task<KaggleResult> WhoAmIAsync(CancellationToken ct) => RunAsync(["whoami"], TimeSpan.FromSeconds(60), ct);

    public Task<KaggleResult> PushAsync(string folder, int timeoutSeconds, string accelerator, CancellationToken ct) =>
        RunAsync(["push", folder, timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), accelerator], TimeSpan.FromMinutes(5), ct);

    public Task<KaggleResult> StatusAsync(string kernel, CancellationToken ct) => RunAsync(["status", kernel], TimeSpan.FromSeconds(60), ct);

    public Task<KaggleResult> DownloadOutputAsync(string kernel, string destination, CancellationToken ct) =>
        RunAsync(["output", kernel, destination], TimeSpan.FromMinutes(60), ct);

    public Task<KaggleResult> LogsAsync(string kernel, CancellationToken ct) => RunAsync(["logs", kernel], TimeSpan.FromMinutes(2), ct);

    private async Task<KaggleResult> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var python = settings.Current.KagglePythonPath;
        var bridge = Path.Combine(ScriptsDirectory, "bridge.py");
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(python, ["-I", bridge, .. args], ct, timeout);
        }
        catch (ExecutableNotFoundException)
        {
            return new KaggleResult(false, $"Python was not found at '{python}'. Install Python and the Kaggle client (pip install kaggle), then set the path in Settings.", false, []);
        }
        catch (TimeoutException ex)
        {
            return new KaggleResult(false, $"The Kaggle API call timed out: {ex.Message}", false, []);
        }

        return Parse(result.StandardOutput, result.StandardErrorTail);
    }

    internal static KaggleResult Parse(string stdout, string stderr)
    {
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith('{'));
        if (line is null)
        {
            var detail = stderr.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            return new KaggleResult(false, $"Unexpected response from the Kaggle bridge{(detail is null ? "." : $": {detail}")}", false, []);
        }

        try
        {
            var json = JsonNode.Parse(line) as JsonObject ?? [];
            return new KaggleResult(
                json["ok"]?.GetValue<bool>() == true,
                json["error"]?.GetValue<string>(),
                json["auth"]?.GetValue<bool>() == true,
                json);
        }
        catch (JsonException)
        {
            return new KaggleResult(false, "The Kaggle bridge returned malformed JSON.", false, []);
        }
    }
}
