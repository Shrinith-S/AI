using Chitra.Application.Abstractions.Prompting;
using Chitra.Application.Projects;
using Chitra.Application.Stories;

namespace Chitra.Tests.Unit;

public class StoryPlannerTests
{
    private const string Story = "A poor old man finds a bag of gold but becomes arrogant and eventually loses everything.";

    [Fact]
    public async Task SpecExample_WithCharacter_ProducesFiveCoherentScenes()
    {
        var planner = new RuleBasedStoryPlanner();
        var plan = await planner.PlanAsync(new StoryPlanRequest("Ramu's Gold", Story, 5, 5,
            [new StoryCharacterInfo("Ramu", "65-year-old man, grey beard")], null), CancellationToken.None);

        Assert.Equal(5, plan.Scenes.Count);
        Assert.Equal("Ramu", plan.Protagonist);
        Assert.Contains("Introduction", plan.Scenes[0].Title);
        Assert.Contains("Ramu finds a bag of gold", plan.Scenes[1].Action);
        Assert.Contains("Ramu becomes arrogant", plan.Scenes[2].Action);
        Assert.Contains("Ramu loses everything", plan.Scenes[3].Action);
        Assert.Contains("Ending", plan.Scenes[4].Title);

        Assert.Equal("wonder and excitement", plan.Scenes[1].Mood);
        Assert.Equal("tense and dramatic", plan.Scenes[2].Mood);
        Assert.Equal("melancholic and somber", plan.Scenes[3].Mood);
        Assert.All(plan.Scenes, s => Assert.Contains("Ramu", s.CharacterNames));
        Assert.All(plan.Scenes, s => Assert.False(string.IsNullOrWhiteSpace(s.ImagePrompt)));
        Assert.All(plan.Scenes, s => Assert.False(string.IsNullOrWhiteSpace(s.VideoPrompt)));
        Assert.All(plan.Scenes, s => Assert.Equal(5, s.DurationSeconds));
        Assert.Equal("In a quiet village, there lived a poor old man named Ramu.", plan.Scenes[0].Narration);
    }

    [Fact]
    public async Task WithoutCharacters_UsesSubjectFromStory()
    {
        var plan = await new RuleBasedStoryPlanner().PlanAsync(new StoryPlanRequest("Gold", Story, 5, 4, [], null), CancellationToken.None);

        Assert.Equal("The old man", plan.Protagonist);
        Assert.StartsWith("The old man becomes arrogant", plan.Scenes[2].Action);
        Assert.All(plan.Scenes, s => Assert.Empty(s.CharacterNames));
    }

    [Fact]
    public async Task MoreScenesThanBeats_AddsReactionShots_FewerMergesBeats()
    {
        var planner = new RuleBasedStoryPlanner();
        var eight = await planner.PlanAsync(new StoryPlanRequest("Gold", Story, 8, 5, [], null), CancellationToken.None);
        Assert.Equal(8, eight.Scenes.Count);
        Assert.Contains(eight.Scenes, s => s.Title.Contains("reaction"));

        var three = await planner.PlanAsync(new StoryPlanRequest("Gold", Story, 3, 5, [], null), CancellationToken.None);
        Assert.Equal(3, three.Scenes.Count);
        Assert.Contains("loses everything", string.Join(" ", three.Scenes.Select(s => s.Action)));
    }

    [Fact]
    public async Task DetectsLocationFromStory()
    {
        var plan = await new RuleBasedStoryPlanner().PlanAsync(
            new StoryPlanRequest("Temple", "A beggar waits outside the temple. A merchant gives him a coin. He buys food at the market.", 5, 5, [], null),
            CancellationToken.None);

        Assert.Equal("an old stone temple courtyard", plan.Scenes[1].Location);
        Assert.Contains(plan.Scenes, s => s.Location == "a busy village market");
    }

    [Theory]
    [InlineData(768, 432, 1024, 576)]
    [InlineData(432, 768, 576, 1024)]
    [InlineData(512, 512, 1024, 1024)]
    public void KeyframeSize_MatchesProjectAspect(int w, int h, int ew, int eh) =>
        Assert.Equal((ew, eh), SceneService.KeyframeSize(w, h));
}
