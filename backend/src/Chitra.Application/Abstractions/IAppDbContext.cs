using Chitra.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chitra.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Project> Projects { get; }
    DbSet<Character> Characters { get; }
    DbSet<Generation> Generations { get; }
    DbSet<GenerationJob> GenerationJobs { get; }
    DbSet<Scene> Scenes { get; }
    DbSet<MediaAsset> MediaAssets { get; }
    DbSet<PromptTemplate> PromptTemplates { get; }
    DbSet<Style> Styles { get; }
    DbSet<ProviderConfiguration> ProviderConfigurations { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<VideoExport> VideoExports { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The user making the current request. When authentication is disabled this is the local user.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string DisplayName { get; }
}
