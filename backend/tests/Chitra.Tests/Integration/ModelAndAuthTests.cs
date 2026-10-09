using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chitra.Application.Abstractions;
using Chitra.Application.Dtos;
using Chitra.Infrastructure.Ai;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Chitra.Tests.Integration.GenerationPipelineTests;

namespace Chitra.Tests.Integration;

public class ModelAndAuthTests
{
    /// <summary>Serves fake model bytes for HEAD/GET so no real multi-GB download happens in tests.</summary>
    private sealed class FakeModelServer : HttpMessageHandler
    {
        public static readonly byte[] Payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : Payload) };
            response.Content.Headers.ContentLength = Payload.Length;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ModelDownload_ShowsPlan_RequiresConfirmation_AndWritesToModelsFolder()
    {
        string? modelsDir = null;
        await using var factory = new StudioFactory(b => b.ConfigureTestServices(s =>
            s.AddHttpClient<IModelDownloadService, ModelDownloadService>().ConfigurePrimaryHttpMessageHandler(() => new FakeModelServer())));
        var client = factory.CreateClient();

        // Without a models folder the plan explains what to configure.
        var plan = (await client.GetFromJsonAsync<ModelDownloadPlanDto>("/api/models/sd15/download-plan", Json))!;
        Assert.False(plan.CanDownload);
        Assert.Contains("models folder", plan.Reason);

        modelsDir = Path.Combine(factory.Root, "comfy-models");
        Directory.CreateDirectory(modelsDir);
        var settings = (await client.GetFromJsonAsync<JsonElement>("/api/settings")).GetProperty("settings");
        var updated = JsonSerializer.Deserialize<Dictionary<string, object?>>(settings.GetRawText())!;
        updated["comfyUiModelsPath"] = modelsDir;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", updated)).StatusCode);

        plan = (await client.GetFromJsonAsync<ModelDownloadPlanDto>("/api/models/sd15/download-plan", Json))!;
        Assert.True(plan.CanDownload, plan.Reason);
        var file = Assert.Single(plan.Files);
        Assert.True(file.SizeIsExact);
        Assert.Equal(FakeModelServer.Payload.Length, file.SizeBytes);
        Assert.Equal(Path.Combine(modelsDir, "checkpoints", "v1-5-pruned-emaonly-fp16.safetensors"), file.TargetPath);

        // Downloads never start without explicit confirmation.
        var unconfirmed = await client.PostAsJsonAsync("/api/models/sd15/download", new { confirm = false }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        var started = await client.PostAsJsonAsync("/api/models/sd15/download", new { confirm = true }, Json);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        ModelDownloadStatusDto? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await client.GetFromJsonAsync<ModelDownloadStatusDto>("/api/models/sd15/download", Json);
            if (status!.State is "Completed" or "Failed") break;
            await Task.Delay(50);
        }

        Assert.Equal("Completed", status!.State);
        Assert.Equal(FakeModelServer.Payload, await File.ReadAllBytesAsync(file.TargetPath!));
        Assert.False(File.Exists(file.TargetPath + ".part"));
    }

    [Fact]
    public async Task JwtMode_RequiresAuthentication_ButHealthStaysOpen()
    {
        await using var factory = new StudioFactory(b =>
        {
            b.UseSetting("Auth:Mode", "Jwt");
            b.UseSetting("Auth:Authority", "https://login.example.invalid/tenant/v2.0");
            b.UseSetting("Auth:Audience", "api://chitra");
        });
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/generations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }
}
