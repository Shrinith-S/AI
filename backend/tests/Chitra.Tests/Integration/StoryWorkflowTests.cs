using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chitra.Application.Dtos;
using Chitra.Domain.Enums;
using static Chitra.Tests.Integration.GenerationPipelineTests;

namespace Chitra.Tests.Integration;

public class StoryWorkflowTests
{
    [Fact]
    public async Task Character_Story_SceneGeneration_AndTimelineEditing()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        // Character
        var characterResponse = await client.PostAsJsonAsync("/api/characters", new
        {
            name = "Ramu", age = 65, gender = "man", appearance = "grey beard, thin face", clothing = "a simple beige kurta and worn sandals",
        }, Json);
        Assert.Equal(HttpStatusCode.Created, characterResponse.StatusCode);
        var ramu = (await characterResponse.Content.ReadFromJsonAsync<CharacterDto>(Json))!;
        Assert.Equal("Ramu, 65-year-old man, grey beard, thin face, wearing a simple beige kurta and worn sandals", ramu.GeneratedPrompt);

        // Story -> project with editable scenes
        var styles = (await client.GetFromJsonAsync<List<StyleDto>>("/api/styles", Json))!;
        var cinematic = styles.First(s => s.Name == "Cinematic");
        var storyResponse = await client.PostAsJsonAsync("/api/stories/generate", new
        {
            title = "My Ramu Story",
            description = "A poor old man finds a bag of gold but becomes arrogant and eventually loses everything.",
            sceneCount = 5,
            sceneDurationSeconds = 5,
            styleId = cinematic.Id,
            characterIds = new[] { ramu.Id },
        }, Json);
        Assert.Equal(HttpStatusCode.Created, storyResponse.StatusCode);
        var project = (await storyResponse.Content.ReadFromJsonAsync<ProjectDetailDto>(Json))!;
        Assert.Equal("My Ramu Story", project.Name);
        Assert.Equal(5, project.Scenes.Count);
        Assert.Single(project.Characters);
        Assert.All(project.Scenes, s => Assert.Contains(ramu.Id, s.CharacterIds));

        // Edit a scene before generating
        var first = project.Scenes[0];
        var updated = await client.PutAsJsonAsync($"/api/scenes/{first.Id}", new
        {
            title = first.Title,
            imagePrompt = "Ramu sits near the old temple asking for food",
            videoPrompt = "Ramu slowly looks at passing people while the camera gently moves toward him",
            cameraMovement = "push-in",
            durationSeconds = 6,
            narration = first.Narration,
            characterIds = new[] { ramu.Id },
        }, Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // Keyframe image: character description and project style are injected
        var imageGen = (await (await client.PostAsJsonAsync($"/api/scenes/{first.Id}/generate", new { kind = "image", modelId = "sd15" }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal(first.Id, imageGen.SceneId);
        Assert.Contains("Ramu (65-year-old man, grey beard", imageGen.FinalPrompt);
        Assert.StartsWith("cinematic film still", imageGen.FinalPrompt);
        Assert.Equal((1024, 576), (imageGen.Width, imageGen.Height));
        Assert.Equal(GenerationStatus.Completed, (await WaitForTerminalAsync(client, imageGen.Id)).Status);

        var detail = (await client.GetFromJsonAsync<ProjectDetailDto>($"/api/projects/{project.Id}", Json))!;
        var scene = detail.Scenes.First(s => s.Id == first.Id);
        Assert.NotNull(scene.Image);
        Assert.Equal(GenerationStatus.Completed, scene.ImageGeneration!.Status);
        Assert.Equal(6, scene.DurationSeconds);

        // Animate the keyframe (image-to-video)
        var animate = (await (await client.PostAsJsonAsync($"/api/scenes/{first.Id}/generate", new { kind = "animate", modelId = "ltxv-2b" }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        Assert.Equal(GenerationType.ImageToVideo, animate.Type);
        Assert.Equal(GenerationStatus.Completed, (await WaitForTerminalAsync(client, animate.Id)).Status);
        detail = (await client.GetFromJsonAsync<ProjectDetailDto>($"/api/projects/{project.Id}", Json))!;
        Assert.NotNull(detail.Scenes.First(s => s.Id == first.Id).Video);

        // Animating a scene without a keyframe is refused with a clear message
        var noKeyframe = await client.PostAsJsonAsync($"/api/scenes/{detail.Scenes[1].Id}/generate", new { kind = "animate" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, noKeyframe.StatusCode);

        // Timeline: duplicate, reorder, delete
        var copy = (await (await client.PostAsync($"/api/scenes/{first.Id}/duplicate", null)).Content.ReadFromJsonAsync<SceneDto>(Json))!;
        Assert.Equal(1, copy.Order);
        Assert.NotNull(copy.Video);

        detail = (await client.GetFromJsonAsync<ProjectDetailDto>($"/api/projects/{project.Id}", Json))!;
        var reversed = detail.Scenes.Select(s => s.Id).Reverse().ToArray();
        var reordered = (await (await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenes/reorder", new { sceneIds = reversed }, Json))
            .Content.ReadFromJsonAsync<List<SceneDto>>(Json))!;
        Assert.Equal(reversed, reordered.Select(s => s.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/scenes/{copy.Id}")).StatusCode);
        detail = (await client.GetFromJsonAsync<ProjectDetailDto>($"/api/projects/{project.Id}", Json))!;
        Assert.Equal(5, detail.Scenes.Count);
        Assert.Equal(Enumerable.Range(0, 5), detail.Scenes.Select(s => s.Order));

        // Project list and history filter "stories"
        var projects = (await client.GetFromJsonAsync<List<ProjectSummaryDto>>("/api/projects", Json))!;
        Assert.Equal(5, Assert.Single(projects).SceneCount);
        var stories = (await client.GetFromJsonAsync<PagedResult<GenerationDto>>("/api/generations?filter=stories", Json))!;
        Assert.Equal(2, stories.Total);
    }

    [Fact]
    public async Task Styles_BuiltInsAreReadOnly_CustomStylesAreEditable()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var builtIn = (await client.GetFromJsonAsync<List<StyleDto>>("/api/styles", Json))!.First(s => s.IsBuiltIn);
        var edit = await client.PutAsJsonAsync($"/api/styles/{builtIn.Id}", new { name = "x", promptSuffix = "y" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        var created = await client.PostAsJsonAsync("/api/styles", new { name = "Moody noir", promptPrefix = "black and white film noir" }, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var style = (await created.Content.ReadFromJsonAsync<StyleDto>(Json))!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/styles/{style.Id}")).StatusCode);
    }

    [Fact]
    public async Task UseInProject_AttachesGenerationOutputs()
    {
        await using var factory = new StudioFactory();
        var client = factory.CreateClient();

        var project = (await (await client.PostAsJsonAsync("/api/projects", new { name = "Moodboard" }, Json)).Content.ReadFromJsonAsync<ProjectDetailDto>(Json))!;
        var generation = (await (await client.PostAsJsonAsync("/api/generations/image", new { prompt = "a lotus pond", modelId = "sd15" }, Json))
            .Content.ReadFromJsonAsync<GenerationDto>(Json))!;
        await WaitForTerminalAsync(client, generation.Id);

        var attach = await client.PostAsJsonAsync($"/api/projects/{project.Id}/assets", new { generationId = generation.Id }, Json);
        Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);

        var assets = (await client.GetFromJsonAsync<PagedResult<MediaAssetDto>>($"/api/projects/{project.Id}/assets", Json))!;
        Assert.Equal(1, assets.Total);

        var problem = await client.PostAsJsonAsync("/api/stories/generate", new { title = "", description = "" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, problem.StatusCode);
        Assert.Equal("Validation", (await problem.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
