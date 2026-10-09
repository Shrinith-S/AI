using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Abstractions.Tts;
using Chitra.Application.Settings;
using Chitra.Infrastructure.Ai;
using Chitra.Infrastructure.Ai.ComfyUi;
using Chitra.Infrastructure.Ai.Kaggle;
using Chitra.Infrastructure.Background;
using Chitra.Infrastructure.Media;
using Chitra.Infrastructure.Persistence;
using Chitra.Infrastructure.Storage;
using Chitra.Infrastructure.SystemInfo;
using Chitra.Infrastructure.Tts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chitra.Infrastructure;

public static class DependencyInjection
{
    /// <param name="contentRoot">Base folder for relative storage and SQLite paths.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.Configure<StudioSettings>(configuration.GetSection(StudioSettings.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.PostConfigure<StorageOptions>(o => o.RootPath = ResolvePath(contentRoot, o.RootPath));
        services.Configure<HostInfoOptions>(o => o.DatabaseProvider = configuration[$"{DatabaseOptions.SectionName}:Provider"] ?? "Sqlite");

        AddDatabase(services, configuration, contentRoot);

        services.AddSingleton<IMediaStorage, LocalMediaStorage>();
        services.AddSingleton<IFfmpegService, FfmpegService>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IModelCatalog, JsonModelCatalog>();
        services.AddSingleton<IWorkflowTemplateStore, FileWorkflowTemplateStore>();

        // ComfyUI
        services.AddSingleton<ComfyUiProgressTracker>();
        services.AddHostedService(sp => sp.GetRequiredService<ComfyUiProgressTracker>());
        services.AddHttpClient<ComfyUiClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddScoped<ComfyUiProvider>();
        services.AddScoped<IAiGenerationProvider>(sp => sp.GetRequiredService<ComfyUiProvider>());
        services.AddScoped<IAiProviderRegistry, AiProviderRegistry>();

        // Kaggle batch GPU worker (optional provider; used when Settings → AI provider = Kaggle)
        services.AddSingleton<IKaggleApi, KaggleBridgeApi>();
        services.AddSingleton<KaggleAccount>();
        services.AddSingleton<KaggleJobStore>();
        services.AddSingleton<KaggleRunCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<KaggleRunCoordinator>());
        services.AddScoped<KaggleProvider>();
        services.AddScoped<IAiGenerationProvider>(sp => sp.GetRequiredService<KaggleProvider>());
        services.AddHttpClient<IModelDownloadService, ModelDownloadService>(c =>
        {
            // Model files are many GB; progress, not a timeout, tells the user what is happening.
            c.Timeout = Timeout.InfiniteTimeSpan;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ChitraStudio/1.0");
        });

        // Text to speech
        services.AddScoped<ITtsProvider, PiperTtsProvider>();
        services.AddHttpClient<CoquiTtsProvider>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddScoped<ITtsProvider>(sp => sp.GetRequiredService<CoquiTtsProvider>());
        services.AddScoped<ITtsProviderRegistry, TtsProviderRegistry>();

        // Background processing
        services.AddSingleton<IGenerationQueue, GenerationQueue>();
        services.AddSingleton<IExportQueue, ExportQueue>();
        services.AddHostedService<GenerationWorker>();
        services.AddHostedService<ExportWorker>();

        return services;
    }

    private static void AddDatabase(IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (options.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
            options.Provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = options.PostgresConnectionString
                                   ?? configuration.GetConnectionString("Postgres")
                                   ?? throw new InvalidOperationException("Database:Provider is Postgres but no Database:PostgresConnectionString is set.");
            services.AddDbContext<PostgresAppDbContext>(o => o.UseNpgsql(connectionString, x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
            services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<PostgresAppDbContext>());
        }
        else
        {
            var path = ResolvePath(contentRoot, options.SqlitePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Split queries avoid cartesian explosions when loading projects with scenes and characters.
            services.AddDbContext<SqliteAppDbContext>(o => o.UseSqlite($"Data Source={path}", x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
            services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqliteAppDbContext>());
        }

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DatabaseInitializer>();
    }

    private static string ResolvePath(string contentRoot, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
}
