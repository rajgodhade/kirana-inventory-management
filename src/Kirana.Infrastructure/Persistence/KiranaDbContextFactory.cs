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
    public KiranaDbContext CreateDbContext(string[] args)
    {
        var paths = new AppPaths();
        var options = new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite($"Data Source={paths.DatabaseFilePath}")
            .Options;

        return new KiranaDbContext(options);
    }
}
