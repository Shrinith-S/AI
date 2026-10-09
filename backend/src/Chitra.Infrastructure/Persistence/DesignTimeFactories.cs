using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chitra.Infrastructure.Persistence;

/// <summary>Used only by "dotnet ef" to create SQLite migrations.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

/// <summary>Used only by "dotnet ef" to create PostgreSQL migrations. No connection is opened.</summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=chitra;Username=chitra;Password=design").Options);
}
