using Chitra.Application.Abstractions.Prompting;
using Chitra.Application.Exports;
using Chitra.Application.Generations;
using Chitra.Application.Tts;
using Chitra.Application.Library;
using Chitra.Application.Projects;
using Chitra.Application.Stories;
using Chitra.Application.Media;
using Chitra.Application.Models;
using Chitra.Application.Prompting;
using Chitra.Application.Settings;
using Chitra.Application.SystemStatus;
using Microsoft.Extensions.DependencyInjection;

namespace Chitra.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IRuntimeSettings, RuntimeSettings>();
        services.AddSingleton<IPromptEnhancer, RuleBasedPromptEnhancer>();

        services.AddScoped<SettingsService>();
        services.AddScoped<PromptService>();
        services.AddScoped<MediaService>();
        services.AddScoped<GenerationService>();
        services.AddScoped<GenerationProcessor>();
        services.AddScoped<ModelService>();
        services.AddScoped<SystemStatusService>();

        // Phase 3: characters, styles, projects, scenes and stories
        services.AddSingleton<IStoryPlanner, RuleBasedStoryPlanner>();
        services.AddScoped<CharacterService>();
        services.AddScoped<StyleService>();
        services.AddScoped<PromptTemplateService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<SceneService>();
        services.AddScoped<StoryService>();

        // Phase 4: final video export and narration
        services.AddScoped<ExportService>();
        services.AddScoped<ExportProcessor>();
        services.AddScoped<TtsService>();
        return services;
    }
}
