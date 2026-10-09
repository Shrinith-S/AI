namespace Chitra.Infrastructure;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder for generated media. Relative paths are resolved against the application content root.</summary>
    public string RootPath { get; set; } = "storage";

    /// <summary>Generation and uploads are refused below this much free disk space.</summary>
    public long MinFreeSpaceMb { get; set; } = 1024;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" (default) or "Postgres".</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>SQLite file path; relative paths are resolved against the content root.</summary>
    public string SqlitePath { get; set; } = "data/chitra.db";
    public string? PostgresConnectionString { get; set; }
}

public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>Model catalog file; relative paths are resolved against the application base directory.</summary>
    public string ModelCatalogPath { get; set; } = "ai/models.json";

    /// <summary>Folder of ComfyUI API-format workflow templates.</summary>
    public string WorkflowsPath { get; set; } = "ai/workflows";
}
