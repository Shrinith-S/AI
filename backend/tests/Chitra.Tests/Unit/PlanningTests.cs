using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Generations;

namespace Chitra.Tests.Unit;

public class PlanningTests
{
    private static AiModelDefinition Ltxv => new()
    {
        Id = "ltxv", Name = "LTXV",
        Defaults = new ModelDefaults { MaxFrames = 257, FrameMultiple = 8, DimensionMultiple = 32, MaxDimension = 1280 },
    };

    private static AiModelDefinition Wan => new()
    {
        Id = "wan", Name = "Wan",
        Defaults = new ModelDefaults { MaxFrames = 81, FrameMultiple = 4, DimensionMultiple = 16 },
    };

    [Theory]
    [InlineData(5, 24, 121)]
    [InlineData(3, 24, 73)]
    [InlineData(10, 24, 241)]
    [InlineData(3, 12, 33)]
    public void Frames_FollowEightNPlusOne(double seconds, int fps, int expected)
    {
        var plan = GenerationPlanning.PlanFrames(Ltxv, seconds, fps);
        Assert.Equal(expected, plan.FrameCount);
        Assert.Equal(0, (plan.FrameCount - 1) % 8);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Frames_AreClampedToModelMaximum_WithWarning()
    {
        var plan = GenerationPlanning.PlanFrames(Wan, 10, 24);
        Assert.Equal(81, plan.FrameCount);
        Assert.Single(plan.Warnings);
        Assert.Contains("at most 81 frames", plan.Warnings[0]);
    }

    [Fact]
    public void Size_IsRoundedToModelMultiple_WithWarning()
    {
        var plan = GenerationPlanning.PlanSize(Ltxv, 768, 432);
        Assert.Equal((768, 448), (plan.Width, plan.Height));
        Assert.Single(plan.Warnings);

        var exact = GenerationPlanning.PlanSize(Ltxv, 1024, 576);
        Assert.Equal((1024, 576), (exact.Width, exact.Height));
        Assert.Empty(exact.Warnings);
    }

    [Fact]
    public void RandomSeed_FitsJavaScriptSafeInteger()
    {
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(GenerationPlanning.RandomSeed(), 0, 1L << 53);
        }
    }
}
