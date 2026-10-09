using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chitra.Application.Dtos;
using Chitra.Domain.Enums;

namespace Chitra.Tests.Integration;

public class GenerationPipelineTests
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task TextToImage_RunsThroughQueue_AndStoresOutput()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/generations/image",
            new { prompt = "a red fox in snow", modelId = "sdxl-base", width = 832, height = 1216, seed = 1234 }, Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = (await response.Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal(GenerationStatus.Queued, queued.Status);

        var done = await WaitForTerminalAsync(client, queued.Id);
        Assert.Equal(GenerationStatus.Completed, done.Status);
        Assert.Equal(100, done.Progress);
        var output = Assert.Single(done.Outputs);
        Assert.Equal(MediaType.Image, output.Type);

        var bytes = await client.GetByteArrayAsync(output.Url);
        Assert.Equal(FakeComfyUi.PngBytes, bytes);

        // The real SDXL workflow was rendered with the installed file name (in a sub-folder) and typed values.
        var workflow = Assert.Single(factory.Comfy.SubmittedWorkflows);
        Assert.Equal("SDXL/sd_xl_base_1.0.safetensors", workflow["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
        Assert.Equal(1234, workflow["3"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal(832, workflow["5"]!["inputs"]!["width"]!.GetValue<int>());
        Assert.Equal("a red fox in snow", workflow["6"]!["inputs"]!["text"]!.GetValue<string>());

        var history = await client.GetFromJsonAsync<PagedResult<GenerationDto>>("/api/generations?filter=images", Json);
        Assert.Equal(1, history!.Total);
    }

    [Fact]
    public async Task TextToVideo_PlansFramesAndResolution()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/generations/video", new
        {
            prompt = "waves crashing on rocks",
            modelId = "ltxv-2b",
            durationSeconds = 5,
            width = 768,
            height = 432,
            fps = 24,
            cameraMovement = "push-in",
            motionStrength = 0.6,
        }, Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = (await response.Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal(121, queued.FrameCount);
        Assert.Equal(448, queued.Height);
        Assert.Contains(queued.Warnings, w => w.Contains("768x448"));
        Assert.Contains("the camera slowly pushes in", queued.FinalPrompt);

        var done = await WaitForTerminalAsync(client, queued.Id);
        Assert.Equal(GenerationStatus.Completed, done.Status);
        Assert.Equal(MediaType.Video, Assert.Single(done.Outputs).Type);

        var workflow = Assert.Single(factory.Comfy.SubmittedWorkflows);
        Assert.Equal(121, workflow["6"]!["inputs"]!["length"]!.GetValue<int>());
        Assert.Equal("t5xxl_fp8_e4m3fn.safetensors", workflow["2"]!["inputs"]!["clip_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ImageToVideo_UploadsInputImageToComfyUi()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        using var form = new MultipartFormDataContent { { new ByteArrayContent(FakeComfyUi.PngBytes), "file", "start.png" } };
        var upload = await client.PostAsync("/api/media/upload", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var asset = (await upload.Content.ReadFromJsonAsync<MediaAssetDto>(Json))!;

        var response = await client.PostAsJsonAsync("/api/generations/image-to-video",
            new { prompt = "the character walks forward", modelId = "ltxv-2b", inputAssetId = asset.Id, durationSeconds = 3, fps = 24 }, Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = (await response.Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal(GenerationType.ImageToVideo, queued.Type);

        var done = await WaitForTerminalAsync(client, queued.Id);
        Assert.Equal(GenerationStatus.Completed, done.Status);
        Assert.Equal("upload_0.png", factory.Comfy.SubmittedWorkflows[0]["13"]!["inputs"]!["image"]!.GetValue<string>());
    }

    [Fact]
    public async Task ComfyUiDown_ReturnsServiceUnavailable_AndQueuesNothing()
    {
        await using var factory = new StudioFactory();
        factory.Comfy.Available = false;
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/generations/image", new { prompt = "anything" }, Json);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProviderUnavailable", problem.GetProperty("code").GetString());
        Assert.StartsWith("ComfyUI is not running. Start ComfyUI at http://comfyui-", problem.GetProperty("detail").GetString());

        var history = await client.GetFromJsonAsync<PagedResult<GenerationDto>>("/api/generations", Json);
        Assert.Equal(0, history!.Total);
    }

    [Fact]
    public async Task MissingModel_ReturnsModelMissing()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/generations/image", new { prompt = "x", modelId = "flux-schnell" }, Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ModelMissing", problem.GetProperty("code").GetString());
        Assert.Contains("flux1-schnell-fp8.safetensors", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExecutionError_MarksGenerationFailed_WithMessage()
    {
        await using var factory = new StudioFactory();
        factory.Comfy.ExecutionError = "mat1 and mat2 shapes cannot be multiplied";
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/generations/image", new { prompt = "x", modelId = "sd15" }, Json);
        var queued = (await response.Content.ReadFromJsonAsync<GenerationDto>(Json))!;

        var done = await WaitForTerminalAsync(client, queued.Id);
        Assert.Equal(GenerationStatus.Failed, done.Status);
        Assert.Equal("GenerationFailed", done.ErrorCode);
        Assert.Contains("mat1 and mat2", done.ErrorMessage);
    }

    [Fact]
    public async Task Variation_UsesNewSeed_RegenerateKeepsSeed()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var first = (await (await client.PostAsJsonAsync("/api/generations/image", new { prompt = "x", modelId = "sd15", seed = 7 }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        await WaitForTerminalAsync(client, first.Id);

        var regen = (await (await client.PostAsync($"/api/generations/{first.Id}/regenerate", null)).Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        var variation = (await (await client.PostAsync($"/api/generations/{first.Id}/variation", null)).Content.ReadFromJsonAsync<GenerationDto>(Json))!;

        Assert.Equal(7, regen.Seed);
        Assert.NotEqual(7, variation.Seed);
        Assert.Equal(first.Id, variation.ParentGenerationId);

        await WaitForTerminalAsync(client, regen.Id);
        await WaitForTerminalAsync(client, variation.Id);
        var delete = await client.DeleteAsync($"/api/generations/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/generations/{first.Id}")).StatusCode);
    }

    internal static async Task<GenerationDto> WaitForTerminalAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var generation = (await client.GetFromJsonAsync<GenerationDto>($"/api/generations/{id}", Json))!;
            if (generation.Status.IsTerminal()) return generation;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Generation {id} did not finish.");
    }
}
