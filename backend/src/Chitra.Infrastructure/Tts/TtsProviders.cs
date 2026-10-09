using Chitra.Application.Abstractions.Tts;
using Chitra.Application.Common;
using Chitra.Application.Settings;
using Chitra.Infrastructure.Ai;
using Chitra.Infrastructure.Processes;

namespace Chitra.Infrastructure.Tts;

/// <summary>
/// Piper (https://github.com/rhasspy/piper): fast, fully offline neural TTS. Voices are .onnx files with a matching
/// .onnx.json next to them, placed in the configured voices folder.
/// </summary>
public sealed class PiperTtsProvider(IRuntimeSettings settings) : ITtsProvider
{
    public string Name => "Piper";
    public bool IsLocal => true;
    public bool IncursApiCharges => false;

    public async Task<TtsHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var s = settings.Current;
        try
        {
            await ProcessRunner.RunAsync(s.PiperPath, ["--help"], cancellationToken, TimeSpan.FromSeconds(10));
        }
        catch (ExecutableNotFoundException)
        {
            return new TtsHealth(false,
                $"Piper was not found ('{s.PiperPath}'). Download it from https://github.com/rhasspy/piper/releases and set the Piper path in Settings.");
        }
        catch (TimeoutException)
        {
            return new TtsHealth(false, "Piper did not respond.");
        }

        var voices = await GetVoicesAsync(cancellationToken);
        return voices.Count == 0
            ? new TtsHealth(false, $"Piper is installed but no voices were found in '{s.PiperVoicesPath ?? "(not set)"}'. Download a voice (.onnx + .onnx.json) and set the voices folder in Settings.")
            : new TtsHealth(true, $"Piper is ready with {voices.Count} voice(s).");
    }

    public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        var dir = settings.Current.PiperVoicesPath;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<TtsVoice>>([]);

        IReadOnlyList<TtsVoice> voices = Directory.EnumerateFiles(dir, "*.onnx", SearchOption.AllDirectories)
            .Where(f => File.Exists(f + ".json"))
            .Select(f =>
            {
                var id = Path.GetFileNameWithoutExtension(f);
                var language = id.Split('-').FirstOrDefault();
                return new TtsVoice(id, id.Replace('_', ' ').Replace('-', ' '), language);
            })
            .OrderBy(v => v.Id)
            .ToList();
        return Task.FromResult(voices);
    }

    public async Task SynthesizeAsync(TtsRequest request, string outputWavPath, CancellationToken cancellationToken)
    {
        var s = settings.Current;
        var voices = await GetVoicesAsync(cancellationToken);
        var voiceId = request.VoiceId ?? s.PiperDefaultVoice ?? voices.FirstOrDefault()?.Id
                      ?? throw new AppException(ErrorCode.TtsUnavailable, "No Piper voice is installed. Set the voices folder in Settings.");
        var modelPath = Directory.EnumerateFiles(s.PiperVoicesPath!, $"{voiceId}.onnx", SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new AppException(ErrorCode.TtsUnavailable, $"Piper voice '{voiceId}' was not found in {s.PiperVoicesPath}.");

        var args = new List<string> { "--model", modelPath, "--output_file", outputWavPath };
        if (Math.Abs(request.Speed - 1.0) > 0.01)
        {
            // Piper's length scale is the inverse of speed.
            args.AddRange(["--length_scale", (1.0 / Math.Clamp(request.Speed, 0.5, 2.0)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]);
        }

        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(s.PiperPath, args, cancellationToken, TimeSpan.FromMinutes(5), standardInput: request.Text);
        }
        catch (ExecutableNotFoundException ex)
        {
            throw new AppException(ErrorCode.TtsUnavailable, $"Piper was not found ('{s.PiperPath}'). Set the Piper path in Settings.", ex);
        }

        if (result.ExitCode != 0 || !File.Exists(outputWavPath))
        {
            throw new AppException(ErrorCode.TtsUnavailable, $"Piper failed (exit code {result.ExitCode}): {result.StandardErrorTail}");
        }
    }
}

/// <summary>Coqui TTS via its local HTTP server ("tts-server"). Optional alternative to Piper.</summary>
public sealed class CoquiTtsProvider(HttpClient http, IRuntimeSettings settings) : ITtsProvider
{
    public string Name => "Coqui";
    public bool IsLocal => LocalNetwork.IsLocalUrl(settings.Current.CoquiUrl);
    public bool IncursApiCharges => false;

    public async Task<TtsHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var url = settings.Current.CoquiUrl.TrimEnd('/');
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await http.GetAsync(url + "/", cts.Token);
            return response.IsSuccessStatusCode
                ? new TtsHealth(true, $"Coqui TTS server is running at {url}.")
                : new TtsHealth(false, $"Coqui TTS server at {url} returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new TtsHealth(false, $"Coqui TTS server is not running at {url}. Start it with 'tts-server --model_name <model>'.");
        }
    }

    public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TtsVoice>>([new TtsVoice("default", "Server default voice", null)]);

    public async Task SynthesizeAsync(TtsRequest request, string outputWavPath, CancellationToken cancellationToken)
    {
        var url = settings.Current.CoquiUrl.TrimEnd('/');
        var query = $"text={Uri.EscapeDataString(request.Text)}";
        if (!string.IsNullOrWhiteSpace(request.VoiceId) && request.VoiceId != "default")
        {
            query += $"&speaker_id={Uri.EscapeDataString(request.VoiceId)}";
        }

        try
        {
            using var response = await http.GetAsync($"{url}/api/tts?{query}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AppException(ErrorCode.TtsUnavailable, $"Coqui TTS returned {(int)response.StatusCode}.");
            }

            await using var file = File.Create(outputWavPath);
            await response.Content.CopyToAsync(file, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AppException(ErrorCode.TtsUnavailable, $"Coqui TTS server is not reachable at {url}.", ex);
        }
    }
}

public sealed class TtsProviderRegistry(IEnumerable<ITtsProvider> providers, IRuntimeSettings settings) : ITtsProviderRegistry
{
    public IReadOnlyList<ITtsProvider> All { get; } = providers.ToList();

    public ITtsProvider GetActive()
    {
        var current = settings.Current;
        var provider = All.FirstOrDefault(p => p.Name.Equals(current.TtsProvider, StringComparison.OrdinalIgnoreCase))
                       ?? throw new AppException(ErrorCode.TtsUnavailable,
                           $"TTS provider '{current.TtsProvider}' is not available. Choose one of: {string.Join(", ", All.Select(p => p.Name))}.");

        if (current.LocalOnly && (provider.IncursApiCharges || !provider.IsLocal))
        {
            throw new AppException(ErrorCode.LocalOnlyViolation,
                $"{provider.Name} is configured with a remote endpoint and Local-Only mode is on.");
        }

        return provider;
    }
}
