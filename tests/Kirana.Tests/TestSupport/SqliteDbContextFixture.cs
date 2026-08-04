using Kirana.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.TestSupport;

/// <summary>
/// A real (in-memory) SQLite-backed <see cref="KiranaDbContext"/> for integration-style
/// tests. Uses EnsureCreated rather than migrations — it verifies the entity model and
/// business logic, not migration correctness.
/// </summary>
public sealed class SqliteDbContextFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public KiranaDbContext Context { get; }

    public SqliteDbContextFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new KiranaDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
