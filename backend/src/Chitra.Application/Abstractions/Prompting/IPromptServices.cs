using Chitra.Domain.Enums;

namespace Chitra.Application.Abstractions.Prompting;

/// <summary>
/// Expands a short prompt into a detailed one. The built-in implementation is rule-based and free;
/// LLM-backed enhancers (OpenAI, Claude, Gemini, local LLMs) can be registered alongside it.
/// </summary>
public interface IPromptEnhancer
{
    string Name { get; }
    bool IsLocal { get; }
    bool IncursApiCharges { get; }
    Task<PromptEnhancement> EnhanceAsync(PromptEnhancementRequest request, CancellationToken cancellationToken);
}

public sealed record PromptEnhancementRequest(string Prompt, GenerationType Type, string? StyleHint = null, int Variation = 0);

public sealed record PromptEnhancement(string Prompt, string? NegativePrompt, IReadOnlyList<string> Additions, string Enhancer);

/// <summary>Breaks a story synopsis into editable scenes. The built-in planner is rule-based.</summary>
public interface IStoryPlanner
{
    string Name { get; }
    Task<StoryPlan> PlanAsync(StoryPlanRequest request, CancellationToken cancellationToken);
}

public sealed record StoryCharacterInfo(string Name, string Description);

public sealed record StoryPlanRequest(
    string Title,
    string Description,
    int SceneCount,
    double SceneDurationSeconds,
    IReadOnlyList<StoryCharacterInfo> Characters,
    string? StyleSuffix);

public sealed record StoryPlan(string Title, string Protagonist, IReadOnlyList<ScenePlan> Scenes, string Planner);

public sealed record ScenePlan(
    string Title,
    string Description,
    string Location,
    string Action,
    string Mood,
    IReadOnlyList<string> CharacterNames,
    string ImagePrompt,
    string VideoPrompt,
    string CameraMovement,
    double DurationSeconds,
    string Narration);
