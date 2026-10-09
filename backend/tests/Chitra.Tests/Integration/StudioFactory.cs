using Chitra.Application.Generations;
using Chitra.Infrastructure.Ai.ComfyUi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Chitra.Tests.Integration;

/// <summary>Hosts the real API with a temporary SQLite database and storage folder, and a fake ComfyUI transport.</summary>
public sealed class StudioFactory(Action<IWebHostBuilder>? configure = null) : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chitra-tests", Guid.NewGuid().ToString("N"));

    public string Root => _root;

    public FakeComfyUi Comfy { get; } = new();

    public string StorageRoot => Path.Combine(_root, "storage");

    static StudioFactory()
    {
        GenerationProcessor.PollInterval = TimeSpan.FromMilliseconds(50);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:SqlitePath", Path.Combine(_root, "test.db"));
        builder.UseSetting("Storage:RootPath", StorageRoot);
        builder.UseSetting("Storage:MinFreeSpaceMb", "1");

        // A unique single-label host keeps ComfyUI health/inventory caches separate per test.
        builder.UseSetting("Studio:ComfyUiUrl", $"http://comfyui-{Guid.NewGuid():N}:8188");
        builder.UseSetting("Studio:FfmpegPath", "ffmpeg-not-installed-for-tests");

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<ComfyUiClient>().ConfigurePrimaryHttpMessageHandler(() => Comfy);
        });

        configure?.Invoke(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Temp folder cleanup is best effort.
        }
    }
}
