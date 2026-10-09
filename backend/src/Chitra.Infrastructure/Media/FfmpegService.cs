using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Application.Settings;
using Chitra.Domain.Enums;
using Chitra.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Media;

/// <summary>Wraps the locally installed FFmpeg/ffprobe binaries. No cloud video processing is used.</summary>
public sealed class FfmpegService(IRuntimeSettings settings, ILogger<FfmpegService> logger) : IFfmpegService
{
    private static readonly string[] SupportedTransitions =
        ["fade", "dissolve", "wipeleft", "wiperight", "slideleft", "slideright", "circleopen", "fadeblack", "fadewhite", "smoothleft"];

    private static (DateTime At, string Path, FfmpegStatus Status)? _statusCache;

    private string FfmpegPath => string.IsNullOrWhiteSpace(settings.Current.FfmpegPath) ? "ffmpeg" : settings.Current.FfmpegPath.Trim();

    private string FfprobePath
    {
        get
        {
            var ffmpeg = FfmpegPath;
            var dir = Path.GetDirectoryName(ffmpeg);
            var probeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            return string.IsNullOrEmpty(dir) ? "ffprobe" : Path.Combine(dir, probeName);
        }
    }

    public string UnavailableMessage =>
        $"FFmpeg was not found ('{FfmpegPath}'). Install it (Windows: winget install Gyan.FFmpeg; Linux: apt install ffmpeg) " +
        "and restart the API, or set the full FFmpeg path in Settings.";

    public async Task<FfmpegStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var path = FfmpegPath;
        if (_statusCache is { } cached && cached.Path == path && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(30))
        {
            return cached.Status;
        }

        FfmpegStatus status;
        try
        {
            var result = await ProcessRunner.RunAsync(path, ["-hide_banner", "-version"], cancellationToken, TimeSpan.FromSeconds(10));
            var firstLine = result.StandardOutput.Split('\n').FirstOrDefault()?.Trim();
            var version = firstLine?.Split(' ').Skip(2).FirstOrDefault();
            status = result.ExitCode == 0
                ? new FfmpegStatus(true, version, path, $"FFmpeg {version} is available.")
                : new FfmpegStatus(false, null, path, $"FFmpeg exited with code {result.ExitCode}: {result.StandardErrorTail}");
        }
        catch (ExecutableNotFoundException)
        {
            status = new FfmpegStatus(false, null, path, UnavailableMessage);
        }
        catch (TimeoutException ex)
        {
            status = new FfmpegStatus(false, null, path, ex.Message);
        }

        _statusCache = (DateTime.UtcNow, path, status);
        return status;
    }

    public async Task<MediaProbe?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(FfprobePath,
                ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path], cancellationToken, TimeSpan.FromSeconds(30));
            if (result.ExitCode != 0) return null;

            var json = JsonNode.Parse(result.StandardOutput);
            var streams = json?["streams"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
            var video = streams.FirstOrDefault(s => s["codec_type"]?.GetValue<string>() == "video");
            var durationText = json?["format"]?["duration"]?.GetValue<string>();
            double? duration = double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            return new MediaProbe(
                video?["width"]?.GetValue<int>(),
                video?["height"]?.GetValue<int>(),
                duration,
                streams.Any(s => s["codec_type"]?.GetValue<string>() == "audio"));
        }
        catch (ExecutableNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> TryCreateThumbnailAsync(string inputPath, string outputPath, MediaType type, CancellationToken cancellationToken)
    {
        if (!(await GetStatusAsync(cancellationToken)).IsAvailable) return false;

        var filter = type == MediaType.Video ? "thumbnail=20,scale=480:-2" : "scale=480:-2";
        var result = await ProcessRunner.RunAsync(FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y", "-i", inputPath, "-vf", filter, "-frames:v", "1", "-q:v", "4", outputPath],
            cancellationToken, TimeSpan.FromSeconds(60));
        if (result.ExitCode != 0)
        {
            logger.LogDebug("Thumbnail failed for {Path}: {Error}", inputPath, result.StandardErrorTail);
            return false;
        }

        return File.Exists(outputPath);
    }

    public async Task ExtractLastFrameAsync(string videoPath, string outputPath, CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);

        // Seek near the end and keep overwriting the output so the final decoded frame wins.
        var result = await ProcessRunner.RunAsync(FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y", "-sseof", "-1", "-i", videoPath, "-update", "1", "-q:v", "1", outputPath],
            cancellationToken, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new AppException(ErrorCode.GenerationFailed, $"FFmpeg could not extract the last frame: {result.StandardErrorTail}");
        }
    }

    public async Task ComposeAsync(VideoCompositionRequest request, IProgress<int> progress, CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);
        if (request.Clips.Count == 0) throw new AppException(ErrorCode.Validation, "There are no scenes to export.");

        var outputDir = Path.GetDirectoryName(request.OutputPath)!;
        var workDir = Path.Combine(outputDir, $"tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            var transition = NormalizeTransition(request.Transition);
            var minClip = request.Clips.Min(c => c.DurationSeconds);
            var transitionSeconds = transition is null ? 0 : Math.Clamp(request.TransitionSeconds, 0.1, Math.Max(0.1, minClip / 2));
            var totalSeconds = request.Clips.Sum(c => c.DurationSeconds) - transitionSeconds * (request.Clips.Count - 1);

            // 1. Normalize every clip to the same size, frame rate, pixel format and audio layout (60% of the work).
            var segments = new List<string>();
            for (var i = 0; i < request.Clips.Count; i++)
            {
                var segment = Path.Combine(workDir, $"seg_{i:D3}.mp4");
                var clip = request.Clips[i];
                var start = 60 * i / request.Clips.Count;
                var span = 60.0 / request.Clips.Count;
                await RunFfmpegAsync(BuildSegmentArgs(clip, segment, request), clip.DurationSeconds,
                    p => progress.Report(start + (int)(span * p)), workDir, cancellationToken);
                segments.Add(segment);
            }

            // 2. Join clips, with cross-fades when a transition is selected (20%).
            var joined = Path.Combine(workDir, "joined.mp4");
            if (transition is null || segments.Count == 1)
            {
                var list = Path.Combine(workDir, "list.txt");
                await File.WriteAllLinesAsync(list, segments.Select(s => $"file '{Path.GetFileName(s)}'"), cancellationToken);
                await RunFfmpegAsync(["-f", "concat", "-safe", "0", "-i", "list.txt", "-c", "copy", "-movflags", "+faststart", joined],
                    totalSeconds, p => progress.Report(60 + (int)(20 * p)), workDir, cancellationToken);
            }
            else
            {
                await RunFfmpegAsync(BuildTransitionArgs(segments, request.Clips, transition, transitionSeconds, joined),
                    totalSeconds, p => progress.Report(60 + (int)(20 * p)), workDir, cancellationToken);
            }

            // 3. Background music and burned-in subtitles (20%).
            var hasSubtitles = request.BurnSubtitles && request.Clips.Any(c => !string.IsNullOrWhiteSpace(c.SubtitleText));
            if (request.MusicPath is null && !hasSubtitles)
            {
                File.Move(joined, request.OutputPath, overwrite: true);
            }
            else
            {
                var args = new List<string> { "-i", "joined.mp4" };
                var filters = new List<string>();
                if (request.MusicPath is not null)
                {
                    args.AddRange(["-stream_loop", "-1", "-i", request.MusicPath]);
                    var volume = Math.Clamp(request.MusicVolume, 0, 2).ToString("0.###", CultureInfo.InvariantCulture);
                    filters.Add($"[1:a]volume={volume}[m];[0:a][m]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a]");
                }

                if (hasSubtitles)
                {
                    // The SRT sits in the working directory so no drive letters need escaping inside the filter graph.
                    await File.WriteAllTextAsync(Path.Combine(workDir, "subs.srt"),
                        BuildSrt(request.Clips, transitionSeconds), new UTF8Encoding(false), cancellationToken);
                    filters.Add("[0:v]subtitles=subs.srt:force_style='FontSize=20,Outline=1,MarginV=28'[v]");
                }

                args.AddRange(["-filter_complex", string.Join(';', filters)]);
                args.AddRange(hasSubtitles
                    ? ["-map", "[v]", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"]
                    : ["-map", "0:v:0", "-c:v", "copy"]);
                args.AddRange(request.MusicPath is not null
                    ? ["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]
                    : ["-map", "0:a:0", "-c:a", "copy"]);
                args.AddRange(["-movflags", "+faststart", request.OutputPath]);
                await RunFfmpegAsync(args, totalSeconds, p => progress.Report(80 + (int)(20 * p)), workDir, cancellationToken);
            }

            progress.Report(100);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete temporary export folder {Dir}", workDir);
            }
        }
    }

    internal static List<string> BuildSegmentArgs(CompositionClip clip, string output, VideoCompositionRequest request)
    {
        var d = clip.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var w = request.Width;
        var h = request.Height;
        var args = new List<string>();

        if (clip.IsStillImage)
        {
            args.AddRange(["-loop", "1", "-framerate", request.Fps.ToString(CultureInfo.InvariantCulture), "-t", d, "-i", clip.MediaPath]);
        }
        else
        {
            args.AddRange(["-i", clip.MediaPath]);
        }

        if (clip.NarrationPath is not null)
        {
            args.AddRange(["-i", clip.NarrationPath]);
        }
        else
        {
            args.AddRange(["-f", "lavfi", "-t", d, "-i", "anullsrc=channel_layout=stereo:sample_rate=44100"]);
        }

        // Fit inside the frame, pad to exact size, hold the last frame if the clip is shorter than the scene.
        var video = $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1," +
                    $"fps={request.Fps},format=yuv420p" + (clip.IsStillImage ? string.Empty : $",tpad=stop_mode=clone:stop_duration={d}");

        args.AddRange([
            "-filter_complex", $"[0:v]{video}[v];[1:a]aresample=44100,aformat=channel_layouts=stereo,apad[a]",
            "-map", "[v]", "-map", "[a]", "-t", d,
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-r", request.Fps.ToString(CultureInfo.InvariantCulture),
            "-c:a", "aac", "-b:a", "192k", "-ar", "44100", "-ac", "2",
            output,
        ]);
        return args;
    }

    internal static List<string> BuildTransitionArgs(
        IReadOnlyList<string> segments, IReadOnlyList<CompositionClip> clips, string transition, double t, string output)
    {
        var args = new List<string>();
        foreach (var segment in segments) args.AddRange(["-i", Path.GetFileName(segment)]);

        var filter = new StringBuilder();
        var td = t.ToString("0.###", CultureInfo.InvariantCulture);
        var offset = 0.0;
        var lastV = "[0:v]";
        var lastA = "[0:a]";
        for (var i = 1; i < segments.Count; i++)
        {
            offset += clips[i - 1].DurationSeconds - t;
            var v = $"[v{i}]";
            var a = $"[a{i}]";
            filter.Append($"{lastV}[{i}:v]xfade=transition={transition}:duration={td}:offset={offset.ToString("0.###", CultureInfo.InvariantCulture)}{v};");
            filter.Append($"{lastA}[{i}:a]acrossfade=d={td}{a};");
            lastV = v;
            lastA = a;
        }

        args.AddRange([
            "-filter_complex", filter.ToString().TrimEnd(';'),
            "-map", lastV, "-map", lastA,
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output,
        ]);
        return args;
    }

    internal static string BuildSrt(IReadOnlyList<CompositionClip> clips, double transitionSeconds)
    {
        var sb = new StringBuilder();
        var start = 0.0;
        var cue = 1;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var end = start + clip.DurationSeconds - (i < clips.Count - 1 ? transitionSeconds : 0);
            if (!string.IsNullOrWhiteSpace(clip.SubtitleText))
            {
                sb.AppendLine((cue++).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine($"{Srt(start)} --> {Srt(Math.Max(start + 0.5, end))}");
                sb.AppendLine(clip.SubtitleText.Trim());
                sb.AppendLine();
            }

            start += clip.DurationSeconds - transitionSeconds;
        }

        return sb.ToString();

        static string Srt(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
        }
    }

    private static string? NormalizeTransition(string? transition)
    {
        if (string.IsNullOrWhiteSpace(transition) || transition.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var match = SupportedTransitions.FirstOrDefault(t => t.Equals(transition.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new AppException(ErrorCode.Validation,
            $"Unknown transition '{transition}'. Use one of: none, {string.Join(", ", SupportedTransitions)}.");
    }

    private async Task EnsureAvailableAsync(CancellationToken ct)
    {
        var status = await GetStatusAsync(ct);
        if (!status.IsAvailable) throw new AppException(ErrorCode.FfmpegUnavailable, status.Message);
    }

    private async Task RunFfmpegAsync(List<string> args, double expectedSeconds, Action<double> onProgress, string workDir, CancellationToken ct)
    {
        var fullArgs = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-progress", "pipe:1", "-nostats" };
        fullArgs.AddRange(args);

        var result = await ProcessRunner.RunAsync(FfmpegPath, fullArgs, ct, TimeSpan.FromHours(2), workDir, onOutputLine: line =>
        {
            // -progress emits key=value lines; out_time_us is the position in microseconds.
            if (expectedSeconds > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(12), out var micros) && micros > 0)
            {
                onProgress(Math.Clamp(micros / 1_000_000.0 / expectedSeconds, 0, 1));
            }
        });

        if (result.ExitCode != 0)
        {
            var error = result.StandardErrorTail;
            if (error.Contains("No such filter: 'subtitles'", StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(ErrorCode.FfmpegUnavailable, "This FFmpeg build has no subtitle support (libass). Install a full build, e.g. 'winget install Gyan.FFmpeg'.");
            }

            if (error.Contains("No space left", StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(ErrorCode.DiskFull, "The disk is full; FFmpeg could not write the video.");
            }

            throw new AppException(ErrorCode.GenerationFailed, $"FFmpeg failed (exit code {result.ExitCode}): {error}");
        }
    }
}
