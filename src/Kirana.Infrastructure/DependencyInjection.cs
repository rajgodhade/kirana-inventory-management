using Kirana.Application.Abstractions;
using Kirana.Application.Backup;
using Kirana.Application.Maintenance;
using Kirana.Application.Restore;
using Kirana.Infrastructure.Backup;
using Kirana.Infrastructure.Barcodes;
using Kirana.Infrastructure.Maintenance;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Kirana.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kirana.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();

        services.AddDbContext<KiranaDbContext>((sp, options) =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            options.UseSqlite($"Data Source={paths.DatabaseFilePath}");
        });
        services.AddScoped<IKiranaDbContext>(sp => sp.GetRequiredService<KiranaDbContext>());

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IAuditLogger, EfAuditLogger>();
        services.AddScoped<ISequenceGenerator, EfSequenceGenerator>();
        services.AddSingleton<IBarcodeRenderer, ZXingBarcodeRenderer>();

        // Backup/restore/maintenance need Microsoft.Data.Sqlite directly, so they live here rather
        // than in the Application layer alongside their interfaces.
        services.AddScoped<IBackupService, SqliteBackupService>();
        services.AddScoped<IRestoreService, SqliteRestoreService>();
        services.AddScoped<IDatabaseMaintenanceService, SqliteDatabaseMaintenanceService>();

        return services;
    }

    /// <summary>
    /// Configures the global Serilog logger to write to Kirana's Logs directory (PRD §41).
    /// Must run before any DI-resolved service logs; call from App startup before building
    /// the host, using an <see cref="IAppPaths"/> instance resolved ahead of the container.
    /// </summary>
    public static void ConfigureSerilog(IAppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "kirana-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }
}
