using Chitra.Application.Abstractions.Prompting;
using Chitra.Application.Prompting;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;

namespace Chitra.Tests.Unit;

public class PromptTests
{
    [Fact]
    public async Task RuleBasedEnhancer_ExpandsShortVideoPrompt()
    {
        var enhancer = new RuleBasedPromptEnhancer();
        var result = await enhancer.EnhanceAsync(
            new PromptEnhancementRequest("A boy walking in a forest", GenerationType.TextToVideo, "3d"), CancellationToken.None);

        Assert.StartsWith("A young boy walking slowly in a dense ancient forest", result.Prompt);
        Assert.Contains("soft mist floating above the ground", result.Prompt);
        Assert.Contains("subtle camera movement", result.Prompt);
        Assert.Contains("volumetric lighting", result.Prompt);
        Assert.Contains("3D animation", result.Prompt);
        Assert.NotEmpty(result.Additions);
        Assert.Equal("RuleBased", result.Enhancer);
    }

    [Fact]
    public async Task RuleBasedEnhancer_IsDeterministic()
    {
        var enhancer = new RuleBasedPromptEnhancer();
        var request = new PromptEnhancementRequest("an old man near a temple", GenerationType.TextToImage);
        var a = await enhancer.EnhanceAsync(request, CancellationToken.None);
        var b = await enhancer.EnhanceAsync(request, CancellationToken.None);
        Assert.Equal(a.Prompt, b.Prompt);
        Assert.DoesNotContain("camera movement", a.Prompt);
    }

    [Fact]
    public void Character_BuildsReusablePrompt()
    {
        var ramu = new Character
        {
            Name = "Ramu", Age = 65, Gender = "Man", Appearance = "grey beard, thin face",
            Clothing = "a simple beige kurta and worn sandals", Personality = "proud",
        };

        Assert.Equal("Ramu, 65-year-old man, grey beard, thin face, wearing a simple beige kurta and worn sandals", ramu.BuildPrompt());
    }

    [Fact]
    public void Composer_InjectsCharacterWhereNamed_AndPrependsOthers()
    {
        var ramu = new Character { Name = "Ramu", Age = 65, Gender = "man", Appearance = "grey beard" };
        var sita = new Character { Name = "Sita", Age = 30, Gender = "woman" };

        var composed = PromptComposer.Compose(GenerationType.TextToImage, "Ramu sits outside the temple", null, null, [ramu, sita]);

        Assert.Equal("Sita, 30-year-old woman. Ramu (65-year-old man, grey beard) sits outside the temple", composed.Prompt);
        Assert.Contains("blurry", composed.NegativePrompt);
    }

    [Fact]
    public void Composer_AddsCameraAndMotionForVideo_AndAppliesStyle()
    {
        var style = new Style { PromptPrefix = "cinematic film still", PromptSuffix = "film grain", NegativePrompt = "cartoon, blurry" };
        var composed = PromptComposer.Compose(GenerationType.TextToVideo, "a horse runs", "blurry", style, [], "push-in", 0.9);

        Assert.Equal("cinematic film still, a horse runs, fast energetic motion, dramatic movement, the camera slowly pushes in toward the subject, film grain", composed.Prompt);
        Assert.Equal("blurry, cartoon", composed.NegativePrompt);
    }
}
