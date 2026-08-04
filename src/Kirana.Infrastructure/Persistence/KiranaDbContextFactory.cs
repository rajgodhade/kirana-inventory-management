using Kirana.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kirana.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations` construct a <see cref="KiranaDbContext"/> without the full
/// app DI container. Not used at runtime — the app always resolves the context through
/// <see cref="DependencyInjection.AddInfrastructure"/>.
/// </summary>
public sealed class KiranaDbContextFactory : IDesignTimeDbContextFactory<KiranaDbContext>
{
    /// <summary>
    /// Set to a .db path to point design-time migration commands at a scratch database instead of
    /// the developer's real one — used to rehearse a migration against a copy of a production
    /// database before applying it for real.
    /// </summary>
    public const string DatabasePathOverrideVariable = "KIRANA_MIGRATION_DB";

    public KiranaDbContext CreateDbContext(string[] args)
    {
        var overridePath = Environment.GetEnvironmentVariable(DatabasePathOverrideVariable);
        var databasePath = string.IsNullOrWhiteSpace(overridePath)
            ? new AppPaths().DatabaseFilePath
            : overridePath;

        var options = new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new KiranaDbContext(options);
    }
}
