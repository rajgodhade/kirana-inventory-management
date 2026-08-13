using Kirana.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kirana.Tests.Persistence;

/// <summary>
/// The shared fixtures build their schema with <c>EnsureCreated()</c>, which reads the entity model
/// and ignores the migration files entirely. That leaves a blind spot: a property added to an
/// entity without a matching migration passes every other test and then crashes the real app at
/// startup, because the app runs <c>Migrate()</c> against a database that lacks the column. These
/// tests close that gap by applying the actual migration chain to a throwaway database.
/// </summary>
public sealed class MigrationSchemaTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"kirana-migrations-{Guid.NewGuid():N}.db");

    private KiranaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<KiranaDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    [Fact]
    public async Task MigrationChain_AppliesCleanly_FromEmptyDatabase()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task MigratedSchema_HasEveryColumn_TheEntityModelExpects()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Comparing the migrated database against the model catches drift in either direction, for
        // every entity — not just the one that happened to prompt this test.
        var missing = new List<string>();
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null) continue;

            var actual = await ColumnNamesAsync(context, table);
            if (actual.Count == 0) { missing.Add($"{table} (table missing)"); continue; }

            missing.AddRange(entityType.GetProperties()
                .Select(p => p.GetColumnName())
                .Where(column => column is not null && !actual.Contains(column))
                .Select(column => $"{table}.{column}"));
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task CashRegisterSessions_HasSupplierCashPaymentsColumn()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "CashRegisterSessions");

        Assert.Contains("SupplierCashPayments", columns);
    }

    [Fact]
    public async Task Products_NoLongerHasLegacyIsTaxInclusiveColumn()
    {
        // Regression guard: this NOT NULL column with no default was orphaned when PricingType
        // replaced it (2026-08-08) but never dropped, silently breaking every new product creation
        // against a real migrated database — invisible to the EnsureCreated()-based fixtures other
        // tests use, since they build straight from the current model and never had this column.
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "Products");

        Assert.DoesNotContain("IsTaxInclusive", columns);
    }

    [Fact]
    public async Task Products_HasPurchasePackFieldsColumns()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "Products");

        Assert.Contains("PurchasePackUnit", columns);
        Assert.Contains("PurchasePackSize", columns);
        Assert.Contains("UnitDisplayText", columns);
    }

    [Fact]
    public async Task PurchaseItems_HasPackSnapshotColumns()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "PurchaseItems");

        Assert.Contains("PurchasedPackUnitSnapshot", columns);
        Assert.Contains("PurchasedPackQuantitySnapshot", columns);
    }

    // ---- Phase 13D inventory adjustments ----

    private const string StockCountMigration = "20260813110000_AddStockCounts";

    [Fact]
    public async Task InventoryAdjustmentsTable_ExistsAfterMigrating()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "InventoryAdjustments");
        Assert.Contains("AdjustmentNumber", columns);
        Assert.Contains("Direction", columns);
        Assert.Contains("Reason", columns);
        Assert.Contains("AdjustmentQuantity", columns);
        Assert.Contains("PreviousQuantity", columns);
        Assert.Contains("NewQuantity", columns);
        Assert.Contains("ProductNameSnapshot", columns);
        Assert.Contains("UnitSnapshot", columns);
        Assert.Contains("Notes", columns);
    }

    /// <summary>An "ADJ-…" reference on a stock movement must resolve to exactly one record.</summary>
    [Fact]
    public async Task AdjustmentNumber_IsUniquelyIndexed()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var indexes = await IndexDefinitionsAsync(context, "InventoryAdjustments");
        Assert.Contains(indexes, i =>
            i.Name == "IX_InventoryAdjustments_AdjustmentNumber" && i.Sql?.Contains("UNIQUE") == true);
    }

    /// <summary>Phase 13D is purely additive. In particular it must not disturb the Phase 13C stock
    /// count tables or rewrite any historical stock movement.</summary>
    [Fact]
    public async Task InventoryAdjustmentMigration_LeavesStockCountsAndMovementsUntouched()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(StockCountMigration);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO Products
                    (ProductCode, Name, Unit, PurchasePrice, Mrp, SellingPrice,
                     MinimumStock, ReorderQuantity, TracksBatches, IsActive, CreatedAtUtc, PricingType, GstRatePercent)
                VALUES ('PRD-000001', 'Legacy Product', 'Piece', 10, 15, 14, 0, 0, 0, 1, CURRENT_TIMESTAMP, 'Inclusive', 5);
                INSERT INTO StockMovements
                    (ProductId, MovementType, QuantityChange, PreviousQuantity, NewQuantity, TimestampUtc, CreatedAtUtc)
                VALUES (1, 'StockCountDecrease', -2, 100, 98, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                INSERT INTO StockCounts
                    (CountNumber, Status, StartedAtUtc, RebasedItemCount, CreatedAtUtc)
                VALUES ('STK-COUNT-000001', 'Completed', CURRENT_TIMESTAMP, 0, CURRENT_TIMESTAMP);
                """;
            await seed.ExecuteNonQueryAsync();
        }

        await context.Database.MigrateAsync();

        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT MovementType, QuantityChange, PreviousQuantity, NewQuantity FROM StockMovements";
        await using (var reader = await check.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("StockCountDecrease", reader.GetString(0));
            Assert.Equal(-2m, reader.GetDecimal(1));
            Assert.Equal(100m, reader.GetDecimal(2));
            Assert.Equal(98m, reader.GetDecimal(3));
            Assert.False(await reader.ReadAsync());
        }

        await using var counts = connection.CreateCommand();
        counts.CommandText = "SELECT CountNumber, Status FROM StockCounts";
        await using var countReader = await counts.ExecuteReaderAsync();
        Assert.True(await countReader.ReadAsync());
        Assert.Equal("STK-COUNT-000001", countReader.GetString(0));
        Assert.Equal("Completed", countReader.GetString(1));
    }

    // ---- Phase 13C stock counting ----

    [Fact]
    public async Task StockCountTables_ExistAfterMigrating()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var counts = await ColumnNamesAsync(context, "StockCounts");
        Assert.Contains("CountNumber", counts);
        Assert.Contains("Status", counts);
        Assert.Contains("RebasedItemCount", counts);

        var items = await ColumnNamesAsync(context, "StockCountItems");
        Assert.Contains("SystemQuantity", items);
        Assert.Contains("CountedQuantity", items);
        Assert.Contains("SystemQuantityAtFinalization", items);
        Assert.Contains("ProductNameSnapshot", items);
        Assert.Contains("UnitSnapshot", items);
        Assert.Contains("BarcodeSnapshot", items);
    }

    /// <summary>"Only one count open at a time" and "one item per product per count" are database
    /// invariants, not just service checks — so they survive any future caller that forgets.</summary>
    [Fact]
    public async Task StockCountIndexes_EnforceTheCoreInvariants()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var indexes = await IndexDefinitionsAsync(context, "StockCounts");
        Assert.Contains(indexes, i =>
            i.Name == "IX_StockCounts_SingleInProgress" && i.Sql?.Contains("InProgress") == true);

        var itemIndexes = await IndexDefinitionsAsync(context, "StockCountItems");
        Assert.Contains(itemIndexes, i =>
            i.Name == "IX_StockCountItems_StockCountId_ProductId" && i.Sql?.Contains("UNIQUE") == true);
    }

    /// <summary>Phase 13C is purely additive. A migration that quietly rewrote the ledger while
    /// adding a feature would be far worse than one that failed outright.</summary>
    [Fact]
    public async Task StockCountMigration_LeavesExistingStockMovementsUntouched()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(BarcodeMigration);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO Products
                    (ProductCode, Name, Unit, PurchasePrice, Mrp, SellingPrice,
                     MinimumStock, ReorderQuantity, TracksBatches, IsActive, CreatedAtUtc, PricingType, GstRatePercent)
                VALUES ('PRD-000001', 'Legacy Product', 'Piece', 10, 15, 14, 0, 0, 0, 1, CURRENT_TIMESTAMP, 'Inclusive', 5);
                INSERT INTO StockMovements
                    (ProductId, MovementType, QuantityChange, PreviousQuantity, NewQuantity, TimestampUtc, CreatedAtUtc)
                VALUES (1, 'Purchase', 25, 0, 25, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                """;
            await seed.ExecuteNonQueryAsync();
        }

        await context.Database.MigrateAsync();

        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT MovementType, QuantityChange, PreviousQuantity, NewQuantity FROM StockMovements";
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Purchase", reader.GetString(0));
        Assert.Equal(25m, reader.GetDecimal(1));
        Assert.Equal(0m, reader.GetDecimal(2));
        Assert.Equal(25m, reader.GetDecimal(3));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<List<(string Name, string? Sql)>> IndexDefinitionsAsync(
        KiranaDbContext context, string table)
    {
        var rows = new List<(string, string?)>();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='{table}'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    // ---- Phase 13B barcode backfill ----
    //
    // The one part of this feature no other test can reach: fixtures use EnsureCreated(), which
    // builds from the model and never executes a single line of migration SQL. So the backfill that
    // carries every existing shop's barcodes into the new table is exercised only here — by migrating
    // to the PREVIOUS migration, inserting legacy rows the way the old schema stored them, and then
    // migrating forward.

    private const string PreviousMigration = "20260813091500_DropLegacyProductIsTaxInclusiveColumn";
    private const string BarcodeMigration = "20260813100000_AddProductBarcodes";

    /// <summary>Migrates to the pre-barcode migration and inserts legacy Products rows directly,
    /// since the current entity model no longer has the Barcode column to write through.</summary>
    private async Task SeedLegacyProductsAsync(KiranaDbContext context, params (string Name, string? Barcode)[] products)
    {
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        for (var i = 0; i < products.Length; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Products
                    (ProductCode, Name, Barcode, Unit, PurchasePrice, Mrp, SellingPrice,
                     MinimumStock, ReorderQuantity, TracksBatches, IsActive, CreatedAtUtc)
                VALUES ($code, $name, $barcode, 'Piece', 10, 15, 14, 0, 0, 0, 1, CURRENT_TIMESTAMP);
                """;
            command.Parameters.Add(new SqliteParameter("$code", $"PRD-{i + 1:D6}"));
            command.Parameters.Add(new SqliteParameter("$name", products[i].Name));
            command.Parameters.Add(new SqliteParameter("$barcode", (object?)products[i].Barcode ?? DBNull.Value));
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<(int ProductId, string Value, string Normalized, string Symbology, bool IsPrimary, bool IsActive)>>
        ReadBarcodesAsync(KiranaDbContext context)
    {
        var rows = new List<(int, string, string, string, bool, bool)>();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ProductId, Value, NormalizedValue, Symbology, IsPrimary, IsActive FROM ProductBarcodes ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5)));
        }

        return rows;
    }

    [Fact]
    public async Task BarcodeBackfill_MigratesEachExistingBarcode_AsPrimaryAndActive()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("Tata Salt", "8901030826501"), ("Amul Butter", "ABC-123"));

        await context.Database.MigrateAsync();

        var barcodes = await ReadBarcodesAsync(context);
        Assert.Equal(2, barcodes.Count);
        // Behavior must be preserved one-for-one: whatever scanned before still scans, and label
        // printing still defaults to the same value.
        Assert.All(barcodes, b => Assert.True(b.IsPrimary));
        Assert.All(barcodes, b => Assert.True(b.IsActive));
        Assert.Contains(barcodes, b => b.Value == "8901030826501");
        Assert.Contains(barcodes, b => b.Value == "ABC-123");
    }

    [Fact]
    public async Task BarcodeBackfill_StoresUpperCasedNormalizedValue()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("Mixed Case", "abc-123"));

        await context.Database.MigrateAsync();

        var barcode = Assert.Single(await ReadBarcodesAsync(context));
        Assert.Equal("abc-123", barcode.Value);       // as entered, for display/printing
        Assert.Equal("ABC-123", barcode.Normalized);  // the uniqueness + lookup key
    }

    [Fact]
    public async Task BarcodeBackfill_TrimsSurroundingWhitespace()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("Padded", "  8901030826501  "));

        await context.Database.MigrateAsync();

        var barcode = Assert.Single(await ReadBarcodesAsync(context));
        Assert.Equal("8901030826501", barcode.Value);
    }

    [Fact]
    public async Task BarcodeBackfill_CreatesNoRow_ForNullOrBlankBarcodes()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(
            context, ("No Barcode", null), ("Blank Barcode", "   "), ("Has Barcode", "REAL-1"));

        await context.Database.MigrateAsync();

        var barcode = Assert.Single(await ReadBarcodesAsync(context));
        Assert.Equal("REAL-1", barcode.Value);
    }

    [Fact]
    public async Task BarcodeBackfill_MarksThirteenDigitCodesAsEan13()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("Ean", "8901030826501"), ("Code128", "ABC-123"));

        await context.Database.MigrateAsync();

        var barcodes = await ReadBarcodesAsync(context);
        Assert.Equal("Ean13", barcodes.Single(b => b.Value == "8901030826501").Symbology);
        Assert.Equal("Code128", barcodes.Single(b => b.Value == "ABC-123").Symbology);
    }

    /// <summary>The old index on Products.Barcode was case-SENSITIVE, so "abc123" and "ABC123" could
    /// legally coexist — but they collide under the new case-insensitive uniqueness. The migration
    /// must resolve that itself rather than aborting half-applied and leaving a shop unable to start.</summary>
    [Fact]
    public async Task BarcodeBackfill_SurvivesLegacyCaseOnlyDuplicates_KeepingOneRow()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("First", "abc123"), ("Second", "ABC123"));

        var exception = await Record.ExceptionAsync(() => context.Database.MigrateAsync());

        Assert.Null(exception);
        var barcode = Assert.Single(await ReadBarcodesAsync(context));
        Assert.Equal("ABC123", barcode.Normalized);
    }

    /// <summary>The surviving row must belong to the product it actually came from. A GROUP BY that
    /// mixes MIN(Id) with an unaggregated value column can pair one product's id with another
    /// product's barcode text — pointing a real shelf code at the wrong item.</summary>
    [Fact]
    public async Task BarcodeBackfill_KeepsValueAndProductFromTheSameRow_WhenResolvingDuplicates()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("First", "abc123"), ("Second", "ABC123"));

        await context.Database.MigrateAsync();

        var barcode = Assert.Single(await ReadBarcodesAsync(context));
        var ownerName = await ProductNameAsync(context, barcode.ProductId);

        // "First" was inserted first, so it holds the lower Id and keeps the code; its stored value
        // must therefore be "abc123", not "Second"'s "ABC123".
        Assert.Equal("First", ownerName);
        Assert.Equal("abc123", barcode.Value);
    }

    [Fact]
    public async Task BarcodeBackfill_LeavesNoOrphanBarcodes_PointingAtMissingProducts()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("A", "CODE-A"), ("B", "CODE-B"), ("C", null));

        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM ProductBarcodes pb LEFT JOIN Products p ON p.Id = pb.ProductId WHERE p.Id IS NULL";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task BarcodeMigration_Down_RestoresThePrimaryBarcodeColumn()
    {
        await using var context = CreateContext();
        await SeedLegacyProductsAsync(context, ("Tata Salt", "8901030826501"));
        await context.Database.MigrateAsync();

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var columns = await ColumnNamesAsync(context, "Products");
        Assert.Contains("Barcode", columns);

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Barcode FROM Products WHERE Name = 'Tata Salt'";
        Assert.Equal("8901030826501", await command.ExecuteScalarAsync() as string);
    }

    private static async Task<string> ProductNameAsync(KiranaDbContext context, int productId)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Name FROM Products WHERE Id = {productId}";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<HashSet<string>> ColumnNamesAsync(KiranaDbContext context, string table)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
