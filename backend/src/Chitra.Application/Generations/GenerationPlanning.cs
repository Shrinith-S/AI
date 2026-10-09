using Chitra.Application.Abstractions.Ai;

namespace Chitra.Application.Generations;

public sealed record SizePlan(int Width, int Height, IReadOnlyList<string> Warnings);

public sealed record FramePlan(int FrameCount, double ActualDurationSeconds, IReadOnlyList<string> Warnings);

/// <summary>Adapts requested sizes and durations to what a model can actually produce, recording every adjustment.</summary>
public static class GenerationPlanning
{
    public static SizePlan PlanSize(AiModelDefinition model, int width, int height)
    {
        var d = model.Defaults;
        var multiple = Math.Max(8, d.DimensionMultiple);
        var warnings = new List<string>();

        int Fit(int value)
        {
            var clamped = Math.Clamp(value, 64, d.MaxDimension);
            return Math.Max(multiple, (int)Math.Round(clamped / (double)multiple) * multiple);
        }

        var w = Fit(width);
        var h = Fit(height);
        if (w != width || h != height)
        {
            warnings.Add($"Resolution adjusted from {width}x{height} to {w}x{h} ({model.Name} needs multiples of {multiple}, max {d.MaxDimension}px).");
        }

        return new SizePlan(w, h, warnings);
    }

    /// <summary>
    /// Video models need frame counts of the form <c>multiple * n + 1</c> and have a maximum length.
    /// </summary>
    public static FramePlan PlanFrames(AiModelDefinition model, double durationSeconds, int fps)
    {
        var d = model.Defaults;
        var multiple = Math.Max(1, d.FrameMultiple);
        var warnings = new List<string>();

        var requested = Math.Max(2, (int)Math.Round(durationSeconds * fps));
        var n = Math.Max(1, (int)Math.Round((requested - 1) / (double)multiple));
        var frames = n * multiple + 1;

        if (frames > d.MaxFrames)
        {
            n = Math.Max(1, (d.MaxFrames - 1) / multiple);
            frames = n * multiple + 1;
            warnings.Add(
                $"{model.Name} can generate at most {d.MaxFrames} frames; {durationSeconds:0.#}s at {fps} fps was shortened to " +
                $"{frames} frames (~{frames / (double)fps:0.#}s). Lower the FPS or combine scenes for longer videos.");
        }

        return new FramePlan(frames, Math.Round(frames / (double)fps, 2), warnings);
    }

    public static long RandomSeed() => Random.Shared.NextInt64(0, 1L << 50);
}
