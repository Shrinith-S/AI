using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chitra.Application.Dtos;
using Chitra.Domain.Enums;
using Chitra.Infrastructure.Ai.Kaggle;
using Chitra.Infrastructure.Processes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Chitra.Tests.Integration.GenerationPipelineTests;

namespace Chitra.Tests.Integration;

public class KaggleProviderTests
{
    static KaggleProviderTests()
    {
        KaggleRunCoordinator.TickInterval = TimeSpan.FromMilliseconds(50);
        KaggleRunCoordinator.StatusPollInterval = TimeSpan.Zero;
    }

    private static StudioFactory CreateFactory(FakeKaggleApi kaggle, bool localOnly = false, int batchWindowSeconds = 0, string ffmpeg = "ffmpeg-not-installed-for-tests") =>
        new(b =>
        {
            b.UseSetting("Studio:AiProvider", "Kaggle");
            b.UseSetting("Studio:LocalOnly", localOnly ? "true" : "false");
            b.UseSetting("Studio:KaggleBatchWindowSeconds", batchWindowSeconds.ToString());
            b.UseSetting("Studio:FfmpegPath", ffmpeg);
            b.ConfigureTestServices(s => s.AddSingleton<IKaggleApi>(kaggle));
        });

    [Fact]
    public async Task ImageAndVideo_RunInOneBatch_AndReturnOutputs()
    {
        var kaggle = new FakeKaggleApi();
        await using var factory = CreateFactory(kaggle, batchWindowSeconds: 2);
        var client = factory.CreateClient();

        var image = (await (await client.PostAsJsonAsync("/api/generations/image", new { prompt = "a lotus", modelId = "sd15", seed = 3 }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        var video = (await (await client.PostAsJsonAsync("/api/generations/video",
            new { prompt = "waves at dusk", modelId = "ltxv-2b", durationSeconds = 3, fps = 24, width = 768, height = 448 }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal("Kaggle", image.ProviderName);

        var doneImage = await WaitForTerminalAsync(client, image.Id);
        var doneVideo = await WaitForTerminalAsync(client, video.Id);
        Assert.Equal(GenerationStatus.Completed, doneImage.Status);
        Assert.Equal(GenerationStatus.Completed, doneVideo.Status);
        Assert.Equal(MediaType.Video, Assert.Single(doneVideo.Outputs).Type);
        Assert.Equal(FakeComfyUi.PngBytes, await client.GetByteArrayAsync(doneImage.Outputs[0].Url));
        Assert.Contains(doneImage.Warnings, w => w.Contains("batch mode"));

        // Both jobs shared one run; the push contained only the script and its metadata.
        var manifest = Assert.Single(kaggle.Manifests);
        Assert.Equal(2, manifest["jobs"]!.AsArray().Count);
        Assert.Equal("kernel-metadata.json,run.py", Assert.Single(kaggle.PushedFileLists));
        var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(kaggle.PushedFolders[0], "kernel-metadata.json"))).RootElement;
        Assert.Equal("testuser/chitra-gpu-worker", metadata.GetProperty("id").GetString());
        Assert.True(metadata.GetProperty("is_private").GetBoolean());
        Assert.True(metadata.GetProperty("enable_gpu").GetBoolean());

        // The real workflow templates were rendered with catalog file names (the worker downloads them).
        var videoJob = manifest["jobs"]!.AsArray().First(j => j!["type"]!.GetValue<string>() == "TextToVideo")!;
        Assert.Equal("ltx-video-2b-v0.9.5.safetensors", videoJob["workflow"]!["1"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
        Assert.Equal(73, videoJob["workflow"]!["6"]!["inputs"]!["length"]!.GetValue<int>());
        Assert.Equal(2, videoJob["files"]!.AsArray().Count);

        // No credentials or local paths leak into the script that is uploaded to Kaggle.
        var script = File.ReadAllText(Path.Combine(kaggle.PushedFolders[0], "run.py"));
        Assert.DoesNotContain("KAGGLE_KEY", script);
        Assert.DoesNotContain(factory.StorageRoot, manifest.ToJsonString().Replace("\\\\", "\\"));
    }

    [Fact]
    public async Task ImageToVideo_EmbedsInputImage_ForLoadImage()
    {
        var kaggle = new FakeKaggleApi();
        await using var factory = CreateFactory(kaggle);
        var client = factory.CreateClient();

        using var form = new MultipartFormDataContent { { new ByteArrayContent(FakeComfyUi.PngBytes), "file", "keyframe.png" } };
        var asset = (await (await client.PostAsync("/api/media/upload", form)).Content.ReadFromJsonAsync<MediaAssetDto>(Json))!;
        var g = (await (await client.PostAsJsonAsync("/api/generations/image-to-video",
            new { prompt = "the man turns his head", modelId = "ltxv-2b", inputAssetId = asset.Id, durationSeconds = 3 }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal(GenerationStatus.Completed, (await WaitForTerminalAsync(client, g.Id)).Status);

        var job = kaggle.Manifests[0]["jobs"]![0]!;
        var input = Assert.Single(job["inputs"]!.AsArray())!;
        var name = input["name"]!.GetValue<string>();
        Assert.Equal(name, job["workflow"]!["13"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal(FakeComfyUi.PngBytes, Convert.FromBase64String(input["dataB64"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(FakeKaggleMode.QuotaOnPush, "ProviderUnavailable", "quota")]
    [InlineData(FakeKaggleMode.OutOfMemory, "InsufficientVram", "out of memory")]
    public async Task Failures_AreReportedWithUsefulMessages(FakeKaggleMode mode, string code, string text)
    {
        var kaggle = new FakeKaggleApi { Mode = mode };
        await using var factory = CreateFactory(kaggle);
        var client = factory.CreateClient();

        var g = (await (await client.PostAsJsonAsync("/api/generations/image", new { prompt = "x", modelId = "sd15" }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        var done = await WaitForTerminalAsync(client, g.Id);

        Assert.Equal(GenerationStatus.Failed, done.Status);
        Assert.Equal(code, done.ErrorCode);
        Assert.Contains(text, done.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionKilledMidRun_KeepsFinishedJob_FailsTheRest()
    {
        var kaggle = new FakeKaggleApi { Mode = FakeKaggleMode.SessionKilled };
        await using var factory = CreateFactory(kaggle, batchWindowSeconds: 2);
        var client = factory.CreateClient();

        var first = (await (await client.PostAsJsonAsync("/api/generations/image", new { prompt = "one", modelId = "sd15" }, Json)).Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        var second = (await (await client.PostAsJsonAsync("/api/generations/image", new { prompt = "two", modelId = "sd15" }, Json)).Content.ReadFromJsonAsync<GenerationDto>(Json))!;

        Assert.Equal(GenerationStatus.Completed, (await WaitForTerminalAsync(client, first.Id)).Status);
        var failed = await WaitForTerminalAsync(client, second.Id);
        Assert.Equal(GenerationStatus.Failed, failed.Status);
        Assert.Contains("session ended before this job finished (Session timed out)", failed.ErrorMessage);
    }

    [Fact]
    public async Task LocalOnly_BlocksKaggle_AndModelsReflectKaggleGpu()
    {
        await using (var blocked = CreateFactory(new FakeKaggleApi(), localOnly: true))
        {
            var response = await blocked.CreateClient().PostAsJsonAsync("/api/generations/image", new { prompt = "x", modelId = "sd15" }, Json);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("uploaded", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        }

        await using var factory = CreateFactory(new FakeKaggleApi());
        var client = factory.CreateClient();
        var models = (await client.GetFromJsonAsync<List<ModelDto>>("/api/models", Json))!;
        Assert.All(models, m => Assert.Equal("OnDemand", m.InstallState));
        Assert.Equal("Recommended", models.First(m => m.Id == "ltxv-2b").Readiness);
        Assert.Equal("Insufficient", models.First(m => m.Id == "hunyuan-t2v").Readiness);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/system/status");
        Assert.True(status.GetProperty("aiProvider").GetProperty("isAvailable").GetBoolean());
        Assert.Contains("Kaggle batch mode as @testuser", status.GetProperty("aiProvider").GetProperty("message").GetString());

        // HunyuanVideo needs more memory than one T4 has: refused with an explanation, not silently attempted.
        var g = (await (await client.PostAsJsonAsync("/api/generations/video", new { prompt = "x", modelId = "hunyuan-t2v", durationSeconds = 3 }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        var done = await WaitForTerminalAsync(client, g.Id);
        Assert.Equal("InsufficientVram", done.ErrorCode);
        Assert.Contains("not combined across the two GPUs", done.ErrorMessage);
    }

    /// <summary>A clip produced by the Kaggle provider flows into the existing FFmpeg export. Needs FFmpeg.</summary>
    [FfmpegFact]
    public async Task KaggleClip_ExportsThroughFfmpegPipeline()
    {
        var clip = Path.Combine(Path.GetTempPath(), $"chitra-kaggle-clip-{Guid.NewGuid():N}.mp4");
        await ProcessRunner.RunAsync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=768x448:rate=24:duration=3", "-pix_fmt", "yuv420p", clip], CancellationToken.None);
        var kaggle = new FakeKaggleApi { VideoBytes = await File.ReadAllBytesAsync(clip) };
        File.Delete(clip);

        await using var factory = CreateFactory(kaggle, ffmpeg: "ffmpeg");
        var client = factory.CreateClient();

        var project = (await (await client.PostAsJsonAsync("/api/stories/generate",
            new { title = "Kaggle test", description = "A boat sails home.", sceneCount = 3, sceneDurationSeconds = 3 }, Json))
            .Content.ReadFromJsonAsync<ProjectDetailDto>(Json))!;
        foreach (var scene in project.Scenes)
        {
            var g = (await (await client.PostAsJsonAsync($"/api/scenes/{scene.Id}/generate", new { kind = "video", modelId = "ltxv-2b" }, Json))
                .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
            Assert.Equal(GenerationStatus.Completed, (await WaitForTerminalAsync(client, g.Id)).Status);
        }

        var export = (await (await client.PostAsJsonAsync($"/api/projects/{project.Id}/exports", new { resolution = 480, transition = "fade" }, Json))
            .Content.ReadFromJsonAsync<VideoExportDto>(Json))!;
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && !export.Status.IsTerminal())
        {
            await Task.Delay(250);
            export = (await client.GetFromJsonAsync<VideoExportDto>($"/api/exports/{export.Id}", Json))!;
        }

        Assert.Equal(GenerationStatus.Completed, export.Status);
        Assert.NotNull(export.Output);
        Assert.Equal(480, export.Output!.Height);
    }

    /// <summary>
    /// The real worker.py executes the real payload generated by C# against a local ComfyUI (CPU, SD 1.5).
    /// Set CHITRA_TEST_COMFYUI=C:\ComfyUI to run; skipped otherwise.
    /// </summary>
    [LocalComfyUiFact]
    public async Task RealWorkerScript_RunsGeneratedPayload_OnLocalComfyUi()
    {
        var dir = Environment.GetEnvironmentVariable("CHITRA_TEST_COMFYUI")!;
        var kaggle = new FakeKaggleApi { LocalComfyUiDir = dir, LocalComfyUiPython = Path.Combine(dir, ".venv", "Scripts", "python.exe") };
        await using var factory = CreateFactory(kaggle);
        var client = factory.CreateClient();

        var g = (await (await client.PostAsJsonAsync("/api/generations/image",
            new { prompt = "a red lantern on a wooden table", modelId = "sd15", width = 256, height = 256, steps = 4, seed = 11 }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;

        var deadline = DateTime.UtcNow.AddMinutes(10);
        GenerationDto done;
        do
        {
            await Task.Delay(500);
            done = (await client.GetFromJsonAsync<GenerationDto>($"/api/generations/{g.Id}", Json))!;
        }
        while (!done.Status.IsTerminal() && DateTime.UtcNow < deadline);

        Assert.True(done.Status == GenerationStatus.Completed, $"{done.Status}: {done.ErrorMessage}\n{kaggle.LocalWorkerLog}");
        var output = Assert.Single(done.Outputs);
        Assert.Equal(MediaType.Image, output.Type);
        var bytes = await client.GetByteArrayAsync(output.Url);
        Assert.Equal(FakeComfyUi.PngBytes[..8], bytes[..8]); // PNG signature
        Assert.Contains("Job ", kaggle.LocalWorkerLog);
    }
}

public sealed class LocalComfyUiFactAttribute : FactAttribute
{
    public LocalComfyUiFactAttribute()
    {
        var dir = Environment.GetEnvironmentVariable("CHITRA_TEST_COMFYUI");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "main.py")))
        {
            Skip = "Set CHITRA_TEST_COMFYUI to a local ComfyUI checkout (with SD 1.5 installed) to run the real worker test.";
        }
    }
}
