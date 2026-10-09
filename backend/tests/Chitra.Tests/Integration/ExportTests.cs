using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chitra.Application.Abstractions;
using Chitra.Application.Dtos;
using Chitra.Application.Settings;
using Chitra.Infrastructure.Media;
using Chitra.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Chitra.Tests.Integration.GenerationPipelineTests;

namespace Chitra.Tests.Integration;

public class ExportTests
{
    [Fact]
    public async Task Export_RequiresMediaForEveryScene_AndFfmpeg()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var project = (await (await client.PostAsJsonAsync("/api/stories/generate",
            new { title = "T", description = "A girl plants a seed and the tree grows.", sceneCount = 3 }, Json))
            .Content.ReadFromJsonAsync<ProjectDetailDto>(Json))!;

        var noMedia = await client.PostAsJsonAsync($"/api/projects/{project.Id}/exports", new { resolution = 720 }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, noMedia.StatusCode);
        Assert.Contains("no video or keyframe", (await noMedia.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        foreach (var scene in project.Scenes)
        {
            var g = (await (await client.PostAsJsonAsync($"/api/scenes/{scene.Id}/generate", new { kind = "image", modelId = "sd15" }, Json))
                .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
            await WaitForTerminalAsync(client, g.Id);
        }

        // The test host points FFmpeg at a non-existent executable, so the real error must surface.
        var noFfmpeg = await client.PostAsJsonAsync($"/api/projects/{project.Id}/exports", new { resolution = 720, transition = "fade" }, Json);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noFfmpeg.StatusCode);
        var problem = await noFfmpeg.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FfmpegUnavailable", problem.GetProperty("code").GetString());
        Assert.Contains("FFmpeg was not found", problem.GetProperty("detail").GetString());

        var badTransition = await client.PostAsJsonAsync($"/api/projects/{project.Id}/exports", new { resolution = 720, transition = "spin" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, badTransition.StatusCode);
    }

    [Fact]
    public async Task Tts_ReportsMissingPiper_AndNarrationFailsClearly()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var status = (await client.GetFromJsonAsync<TtsStatusDto>("/api/tts/status", Json))!;
        Assert.Equal("Piper", status.Provider);
        Assert.False(status.IsAvailable);

        var project = (await (await client.PostAsJsonAsync("/api/stories/generate",
            new { title = "T", description = "A girl plants a seed and the tree grows.", sceneCount = 3 }, Json))
            .Content.ReadFromJsonAsync<ProjectDetailDto>(Json))!;
        var narrated = project.Scenes.First(s => s.Narration is not null);

        var response = await client.PostAsJsonAsync($"/api/scenes/{narrated.Id}/narration", new { }, Json);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("TtsUnavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    /// <summary>Real FFmpeg run: two generated clips + a still, cross-fades, subtitles and music. Skipped when FFmpeg is absent.</summary>
    [FfmpegFact]
    public async Task Compose_WithRealFfmpeg_ProducesPlayableMp4()
    {
        var dir = Directory.CreateTempSubdirectory("chitra-ffmpeg-").FullName;
        try
        {
            async Task Make(params string[] args) =>
                Assert.Equal(0, (await ProcessRunner.RunAsync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", .. args], CancellationToken.None)).ExitCode);

            var a = Path.Combine(dir, "a.mp4");
            var b = Path.Combine(dir, "b.mp4");
            var still = Path.Combine(dir, "still.png");
            var tone = Path.Combine(dir, "tone.wav");
            await Make("-f", "lavfi", "-i", "testsrc=size=768x432:rate=24:duration=2", "-pix_fmt", "yuv420p", a);
            await Make("-f", "lavfi", "-i", "testsrc2=size=512x512:rate=16:duration=3", "-pix_fmt", "yuv420p", b);
            await Make("-f", "lavfi", "-i", "color=c=orange:size=640x360", "-frames:v", "1", still);
            await Make("-f", "lavfi", "-i", "sine=frequency=440:duration=5", tone);

            var settings = new RuntimeSettings(Options.Create(new StudioSettings()));
            var ffmpeg = new FfmpegService(settings, NullLogger<FfmpegService>.Instance);
            var output = Path.Combine(dir, "out.mp4");
            var reported = new List<int>();

            await ffmpeg.ComposeAsync(new VideoCompositionRequest
            {
                Clips =
                [
                    new CompositionClip(a, false, 3, null, "The beginning."),
                    new CompositionClip(still, true, 2, tone, null),
                    new CompositionClip(b, false, 3, null, "The end."),
                ],
                OutputPath = output,
                Width = 1280,
                Height = 720,
                Fps = 24,
                Transition = "fade",
                TransitionSeconds = 0.5,
                MusicPath = tone,
                BurnSubtitles = true,
            }, new Progress<int>(reported.Add), CancellationToken.None);

            var probe = await ffmpeg.ProbeAsync(output, CancellationToken.None);
            Assert.NotNull(probe);
            Assert.Equal(1280, probe!.Width);
            Assert.Equal(720, probe.Height);
            Assert.InRange(probe.DurationSeconds!.Value, 6.5, 7.5); // 3 + 2 + 3 - 2 * 0.5
            Assert.True(probe.HasAudio);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        try
        {
            var result = ProcessRunner.RunAsync("ffmpeg", ["-version"], CancellationToken.None, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (result.ExitCode != 0) Skip = "FFmpeg is not working on this machine.";
        }
        catch (ExecutableNotFoundException)
        {
            Skip = "FFmpeg is not installed; install it to run the real export test.";
        }
    }
}
