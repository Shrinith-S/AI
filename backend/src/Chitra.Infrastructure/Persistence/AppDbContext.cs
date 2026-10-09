using Chitra.Application.Abstractions;
using Chitra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chitra.Infrastructure.Persistence;

/// <summary>
/// Shared model for all database providers. Provider-specific subclasses exist only so each provider keeps
/// its own migrations history.
/// </summary>
public abstract class AppDbContext(DbContextOptions options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Generation> Generations => Set<Generation>();
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<ProviderConfiguration> ProviderConfigurations => Set<ProviderConfiguration>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<VideoExport> VideoExports => Set<VideoExport>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store UTC and read it back as UTC (SQLite otherwise returns DateTimeKind.Unspecified).
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasIndex(x => x.ExternalId).IsUnique();
        });

        b.Entity<Project>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ImageModelId).HasMaxLength(100);
            e.Property(x => x.VideoModelId).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasOne(x => x.Style).WithMany().HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Characters).WithMany(c => c.Projects).UsingEntity("ProjectCharacters");
            e.HasMany(x => x.Scenes).WithOne(s => s.Project).HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assets).WithOne(a => a.Project).HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Character>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Gender).HasMaxLength(50);
            e.HasIndex(x => new { x.UserId, x.Name });
            e.HasOne(x => x.ReferenceAsset).WithMany().HasForeignKey(x => x.ReferenceAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Scene>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.CameraMovement).HasMaxLength(100);
            e.HasIndex(x => new { x.ProjectId, x.Order });
            e.HasMany(x => x.Characters).WithMany(c => c.Scenes).UsingEntity("SceneCharacters");
        });

        b.Entity<Generation>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ModelId).HasMaxLength(100);
            e.Property(x => x.ProviderName).HasMaxLength(64);
            e.Property(x => x.Sampler).HasMaxLength(64);
            e.Property(x => x.Scheduler).HasMaxLength(64);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.CameraMovement).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SceneId);
            e.HasMany(x => x.Outputs).WithOne(a => a.Generation).HasForeignKey(a => a.GenerationId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Jobs).WithOne(j => j.Generation).HasForeignKey(j => j.GenerationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GenerationJob>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.ProviderName).HasMaxLength(64);
            e.Property(x => x.ProviderJobId).HasMaxLength(128);
            e.HasIndex(x => x.Status);
        });

        b.Entity<MediaAsset>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.OriginalFileName).HasMaxLength(260);
            e.Property(x => x.RelativePath).HasMaxLength(512);
            e.Property(x => x.ThumbnailPath).HasMaxLength(512);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        b.Entity<Style>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        b.Entity<PromptTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasMaxLength(32);
        });

        b.Entity<ProviderConfiguration>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.BaseUrl).HasMaxLength(512);
            e.HasIndex(x => new { x.Name, x.Kind }).IsUnique();
        });

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });

        b.Entity<VideoExport>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Transition).HasMaxLength(32);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}

public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options);

public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options);
