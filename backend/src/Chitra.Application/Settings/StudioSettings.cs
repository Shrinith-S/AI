namespace Chitra.Application.Settings;

/// <summary>
/// User-editable settings. Defaults come from the "Studio" configuration section; values changed in the UI are
/// persisted in the database and override those defaults.
/// </summary>
public sealed class StudioSettings
{
    public const string SectionName = "Studio";

    /// <summary>When true no paid/remote API is ever called.</summary>
    public bool LocalOnly { get; set; } = true;

    public string AiProvider { get; set; } = "ComfyUI";
    public string ComfyUiUrl { get; set; } = "http://localhost:8188";

    /// <summary>ComfyUI "models" directory. Needed only for downloading models from this app.</summary>
    public string? ComfyUiModelsPath { get; set; }

    public string DefaultImageModel { get; set; } = "sdxl-base";
    public string DefaultVideoModel { get; set; } = "ltxv-2b";
    public string DefaultImageToVideoModel { get; set; } = "ltxv-2b";
    public string DefaultImageResolution { get; set; } = "1024x1024";
    public string DefaultVideoResolution { get; set; } = "768x432";
    public int DefaultFps { get; set; } = 24;

    public string FfmpegPath { get; set; } = "ffmpeg";
    public int MaxConcurrentGenerations { get; set; } = 1;

    public string TtsProvider { get; set; } = "Piper";
    public string PiperPath { get; set; } = "piper";
    public string? PiperVoicesPath { get; set; }
    public string? PiperDefaultVoice { get; set; }
    public string CoquiUrl { get; set; } = "http://localhost:5002";

    public string PromptEnhancer { get; set; } = "RuleBased";
    public string Theme { get; set; } = "dark";

    public int ImageTimeoutMinutes { get; set; } = 20;
    public int VideoTimeoutMinutes { get; set; } = 120;

    // Kaggle batch GPU worker (used when AiProvider = "Kaggle"). Credentials are never stored here: the Kaggle client
    // reads them from ~/.kaggle/access_token, ~/.kaggle/kaggle.json or KAGGLE_API_TOKEN.

    /// <summary>Kaggle account name; detected from the credentials when empty.</summary>
    public string? KaggleUsername { get; set; }

    /// <summary>Private notebook (kernel) that Chitra creates and re-runs for each batch.</summary>
    public string KaggleKernelSlug { get; set; } = "chitra-gpu-worker";

    /// <summary>Python interpreter that has the official "kaggle" package installed.</summary>
    public string KagglePythonPath { get; set; } = "python";

    /// <summary>Kaggle accelerator id; NvidiaTeslaT4 is the free "GPU T4 x2".</summary>
    public string KaggleAccelerator { get; set; } = "NvidiaTeslaT4";

    public int KaggleMaxJobsPerRun { get; set; } = 4;

    /// <summary>How long new jobs wait so several can share one run's setup cost.</summary>
    public int KaggleBatchWindowSeconds { get; set; } = 30;

    /// <summary>Kaggle session time limit per run (setup + downloads + all jobs).</summary>
    public int KaggleRunTimeoutMinutes { get; set; } = 120;

    public StudioSettings Clone() => (StudioSettings)MemberwiseClone();

    public static (int Width, int Height) ParseResolution(string? value, (int, int) fallback)
    {
        var parts = value?.ToLowerInvariant().Split('x');
        return parts is { Length: 2 } && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0
            ? (w, h)
            : fallback;
    }
}

/// <summary>Singleton holding the current effective settings for code that runs outside a request.</summary>
public interface IRuntimeSettings
{
    StudioSettings Current { get; }
    void Update(StudioSettings settings);
    event Action<StudioSettings>? Changed;
}

public sealed class RuntimeSettings : IRuntimeSettings
{
    private StudioSettings _current;

    public RuntimeSettings(Microsoft.Extensions.Options.IOptions<StudioSettings> defaults)
    {
        _current = defaults.Value.Clone();
    }

    public StudioSettings Current => Volatile.Read(ref _current).Clone();

    public event Action<StudioSettings>? Changed;

    public void Update(StudioSettings settings)
    {
        Volatile.Write(ref _current, settings.Clone());
        Changed?.Invoke(settings.Clone());
    }
}
