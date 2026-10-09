using System.Text.Json;
using System.Text.Json.Nodes;
using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chitra.Application.Settings;

public sealed record SettingsDto(StudioSettings Settings, string StoragePath, string DatabaseProvider, string AuthMode);

public sealed class SettingsService(
    IAppDbContext db,
    IRuntimeSettings runtime,
    IOptions<StudioSettings> defaults,
    IMediaStorage storage,
    IOptions<HostInfoOptions> hostInfo)
{
    internal const string SettingsKey = "studio.settings";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SettingsDto Get() =>
        new(runtime.Current, storage.RootPath, hostInfo.Value.DatabaseProvider, hostInfo.Value.AuthMode);

    /// <summary>Loads persisted overrides into the runtime snapshot. Called once at startup.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingsKey, cancellationToken);
        var effective = defaults.Value.Clone();
        if (row is not null && JsonNode.Parse(row.Value) is JsonObject overrides)
        {
            var merged = JsonSerializer.SerializeToNode(effective, Json)!.AsObject();
            foreach (var (key, value) in overrides)
            {
                merged[key] = value?.DeepClone();
            }

            effective = merged.Deserialize<StudioSettings>(Json) ?? effective;
        }

        runtime.Update(effective);
    }

    public async Task<SettingsDto> UpdateAsync(StudioSettings settings, CancellationToken cancellationToken)
    {
        Validate(settings);

        // Persist only values that differ from configuration defaults, so later default changes still apply.
        var defaultNode = JsonSerializer.SerializeToNode(defaults.Value, Json)!.AsObject();
        var newNode = JsonSerializer.SerializeToNode(settings, Json)!.AsObject();
        var diff = new JsonObject();
        foreach (var (key, value) in newNode)
        {
            if (!JsonNode.DeepEquals(value, defaultNode[key]))
            {
                diff[key] = value?.DeepClone();
            }
        }

        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsKey, cancellationToken);
        if (row is null)
        {
            row = new AppSetting { Key = SettingsKey };
            db.AppSettings.Add(row);
        }

        row.Value = diff.ToJsonString();
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        runtime.Update(settings);
        return Get();
    }

    private static void Validate(StudioSettings s)
    {
        var v = new Validator();
        v.Require(Uri.TryCreate(s.ComfyUiUrl, UriKind.Absolute, out var comfy) && comfy.Scheme is "http" or "https",
            nameof(s.ComfyUiUrl), "ComfyUI URL must be an absolute http(s) URL, e.g. http://localhost:8188.");
        v.Require(Uri.TryCreate(s.CoquiUrl, UriKind.Absolute, out _), nameof(s.CoquiUrl), "Coqui URL must be an absolute URL.");
        v.Range(s.MaxConcurrentGenerations, 1, 8, nameof(s.MaxConcurrentGenerations));
        v.Require(s.DefaultFps is 8 or 12 or 16 or 24 or 25 or 30, nameof(s.DefaultFps), "Default FPS must be 8, 12, 16, 24, 25 or 30.");
        v.NotEmpty(s.FfmpegPath, nameof(s.FfmpegPath), 1024);
        v.Require(s.AiProvider is "ComfyUI" or "Kaggle", nameof(s.AiProvider), "AI provider must be ComfyUI or Kaggle.");
        if (s.AiProvider == "Kaggle")
        {
            v.Require(string.IsNullOrWhiteSpace(s.KaggleUsername) || System.Text.RegularExpressions.Regex.IsMatch(s.KaggleUsername, "^[A-Za-z0-9_-]{2,50}$"),
                    nameof(s.KaggleUsername), "Kaggle username may contain letters, digits, '-' and '_' only.")
                .Require(System.Text.RegularExpressions.Regex.IsMatch(s.KaggleKernelSlug ?? string.Empty, "^[a-z0-9][a-z0-9-]{4,49}$"),
                    nameof(s.KaggleKernelSlug), "Notebook slug must be 5-50 lowercase letters, digits or dashes.")
                .NotEmpty(s.KagglePythonPath, nameof(s.KagglePythonPath), 1024)
                .Require(System.Text.RegularExpressions.Regex.IsMatch(s.KaggleAccelerator ?? string.Empty, "^[A-Za-z0-9]{3,40}$"),
                    nameof(s.KaggleAccelerator), "Accelerator must be a Kaggle accelerator id such as NvidiaTeslaT4.")
                .Range(s.KaggleMaxJobsPerRun, 1, 10, nameof(s.KaggleMaxJobsPerRun))
                .Range(s.KaggleBatchWindowSeconds, 0, 600, nameof(s.KaggleBatchWindowSeconds))
                .Range(s.KaggleRunTimeoutMinutes, 20, 720, nameof(s.KaggleRunTimeoutMinutes));
        }
        v.Range(s.ImageTimeoutMinutes, 1, 600, nameof(s.ImageTimeoutMinutes));
        v.Range(s.VideoTimeoutMinutes, 1, 1440, nameof(s.VideoTimeoutMinutes));
        v.Require(s.Theme is "dark" or "light" or "system", nameof(s.Theme), "Theme must be dark, light or system.");
        v.Require(StudioSettings.ParseResolution(s.DefaultImageResolution, (0, 0)) != (0, 0),
            nameof(s.DefaultImageResolution), "Resolution must look like 1024x1024.");
        v.Require(StudioSettings.ParseResolution(s.DefaultVideoResolution, (0, 0)) != (0, 0),
            nameof(s.DefaultVideoResolution), "Resolution must look like 768x432.");
        v.ThrowIfInvalid();
    }
}

/// <summary>Read-only facts about how the host was configured, shown on the settings page.</summary>
public sealed class HostInfoOptions
{
    public string DatabaseProvider { get; set; } = "Sqlite";
    public string AuthMode { get; set; } = "None";
}
