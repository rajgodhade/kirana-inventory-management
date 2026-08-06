using Kirana.Application.Abstractions;
using Kirana.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.TestSupport;

/// <summary>
/// A real on-disk SQLite database plus the surrounding Kirana folder layout. Backup, restore and
/// maintenance all operate on actual files — an <c>:memory:</c> database (as used by
/// <see cref="SqliteDbContextFixture"/>) cannot exercise snapshotting, file swapping or VACUUM, so
/// those tests need this instead.
/// </summary>
public sealed class SqliteFileDbContextFixture : IDisposable
{
    public KiranaDbContext Context { get; }

    public TestAppPaths Paths { get; }

    public SqliteFileDbContextFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "kirana-test-" + Guid.NewGuid().ToString("N"));
        Paths = new TestAppPaths(root);

        var options = new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite($"Data Source={Paths.DatabaseFilePath}")
            .Options;

        Context = new KiranaDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>Forces everything staged so far out to the file and drops pooled handles, so a
    /// subsequent backup/restore sees the same bytes the app would.</summary>
    public void Checkpoint()
    {
        Context.ChangeTracker.Clear();
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        Context.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Paths.RootDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class TestAppPaths : IAppPaths
{
    public TestAppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        DataDirectory = Path.Combine(rootDirectory, "Data");
        BackupsDirectory = Path.Combine(rootDirectory, "Backups");
        LogsDirectory = Path.Combine(rootDirectory, "Logs");
        AssetsDirectory = Path.Combine(rootDirectory, "Assets");
        DatabaseFilePath = Path.Combine(DataDirectory, "kirana.db");

        foreach (var dir in new[] { DataDirectory, BackupsDirectory, LogsDirectory, AssetsDirectory })
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string AssetsDirectory { get; }
    public string DatabaseFilePath { get; }
}
