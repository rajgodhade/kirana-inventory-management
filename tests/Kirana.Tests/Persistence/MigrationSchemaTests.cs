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
    public async Task Products_HasDisabledByDefaultReplenishmentConfiguration()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "Products");
        Assert.Contains("ReplenishmentEnabled", columns);
        Assert.Contains("PreferredSupplierId", columns);

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT dflt_value FROM pragma_table_info('Products') WHERE name = 'ReplenishmentEnabled'";
        Assert.Equal("0", Convert.ToString(await command.ExecuteScalarAsync()));
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

    // ---- Phase 15A pricing foundation ----

    /// <summary>The migration immediately before Phase 15A, so the backfill can be exercised against
    /// a database that still holds prices only in the legacy Product columns.</summary>
    private const string PrePricingMigration = "20260813194209_Phase14DReplenishment";

    /// <summary>
    /// Inserts products the way the pre-15A schema stored them — prices in Product columns only,
    /// no ProductPrices table yet — so migrating forward exercises the real backfill rather than a
    /// model-built schema. The fixtures use EnsureCreated() and never run migration SQL, so this is
    /// the only place the backfill is actually executed.
    /// </summary>
    private async Task SeedPreP15ProductsAsync(
        KiranaDbContext context, params (string Name, string Selling, string? Wholesale)[] products)
    {
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PrePricingMigration);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        for (var i = 0; i < products.Length; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Products
                    (ProductCode, Name, Unit, PurchasePrice, Mrp, SellingPrice, WholesalePrice,
                     MinimumStock, ReorderQuantity, TracksBatches, IsActive, CreatedAtUtc,
                     PricingType, GstRatePercent, ReplenishmentEnabled)
                VALUES ($code, $name, 'Piece', 10, 120, $selling, $wholesale, 0, 0, 0, 1,
                        CURRENT_TIMESTAMP, 'Inclusive', 5, 0);
                """;
            command.Parameters.Add(new SqliteParameter("$code", $"PRD-{i + 1:D6}"));
            command.Parameters.Add(new SqliteParameter("$name", products[i].Name));
            command.Parameters.Add(new SqliteParameter("$selling", products[i].Selling));
            command.Parameters.Add(new SqliteParameter("$wholesale", (object?)products[i].Wholesale ?? DBNull.Value));
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Deterministic "ProductId|Retail|Wholesale" fingerprint. <paramref name="fromPrices"/>
    /// selects the ProductPrices table rather than the legacy columns, so the same function can
    /// describe both stores and their outputs compared directly.</summary>
    private static async Task<List<string>> PriceFingerprintAsync(KiranaDbContext context, bool fromPrices)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = fromPrices
            ? """
              SELECT p.Id,
                     (SELECT pr.Price FROM ProductPrices pr
                       WHERE pr.ProductId = p.Id AND pr.Level = 'Retail' AND pr.IsActive = 1),
                     (SELECT pr.Price FROM ProductPrices pr
                       WHERE pr.ProductId = p.Id AND pr.Level = 'Wholesale' AND pr.IsActive = 1)
              FROM Products p ORDER BY p.Id
              """
            : "SELECT p.Id, p.SellingPrice, p.WholesalePrice FROM Products p ORDER BY p.Id";

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var retail = reader.IsDBNull(1) ? "NULL" : reader.GetValue(1).ToString();
            var wholesale = reader.IsDBNull(2) ? "NULL" : reader.GetValue(2).ToString();
            rows.Add($"{reader.GetInt32(0)}|{retail}|{wholesale}");
        }

        return rows;
    }

    private static async Task<long> ScalarAsync(KiranaDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ProductPricesTable_ExistsWithRequiredColumns()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "ProductPrices");
        Assert.Contains("ProductId", columns);
        Assert.Contains("Level", columns);
        Assert.Contains("Price", columns);
        Assert.Contains("IsActive", columns);
        Assert.Contains("CreatedAtUtc", columns);
        Assert.Contains("UpdatedAtUtc", columns);
    }

    /// <summary>"One active price per product per level" must be a database invariant, not just a
    /// service convention.</summary>
    [Fact]
    public async Task ProductPrices_HasFilteredUniqueIndexAndForeignKey()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var indexes = await IndexDefinitionsAsync(context, "ProductPrices");
        var unique = Assert.Single(indexes, i => i.Name == "IX_ProductPrices_ProductId_Level_Active");
        Assert.Contains("UNIQUE", unique.Sql!);
        Assert.Contains("IsActive", unique.Sql!);

        var connection = context.Database.GetDbConnection();
        await using var fk = connection.CreateCommand();
        fk.CommandText = "SELECT \"table\" FROM pragma_foreign_key_list('ProductPrices')";
        Assert.Equal("Products", (string?)await fk.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Backfill_GivesEveryExistingProductExactlyOneRetailPrice()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context,
            ("Alpha", "100", null), ("Beta", "57.50", "49.99"), ("Gamma", "0", null));

        await context.Database.MigrateAsync();

        Assert.Equal(3, await ScalarAsync(context,
            "SELECT COUNT(*) FROM ProductPrices WHERE Level='Retail' AND IsActive=1"));
        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM Products p
            WHERE NOT EXISTS (SELECT 1 FROM ProductPrices pr
                              WHERE pr.ProductId=p.Id AND pr.Level='Retail' AND pr.IsActive=1)
            """));
    }

    [Fact]
    public async Task Backfill_CopiesRetailPricesExactly_WithoutRounding()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context,
            ("Alpha", "100", null), ("Beta", "57.50", null), ("Gamma", "0", null));

        await context.Database.MigrateAsync();

        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM Products p
            JOIN ProductPrices pr ON pr.ProductId = p.Id AND pr.Level='Retail' AND pr.IsActive=1
            WHERE pr.Price <> p.SellingPrice
            """));
    }

    /// <summary>NULL means "wholesale is not configured" and must never become a zero-priced row.</summary>
    [Fact]
    public async Task Backfill_CreatesNoWholesaleRow_ForNullWholesalePrice()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context, ("NoWholesale", "100", null));

        await context.Database.MigrateAsync();

        Assert.Equal(0, await ScalarAsync(context,
            "SELECT COUNT(*) FROM ProductPrices WHERE Level='Wholesale'"));
    }

    /// <summary>...but an explicit zero IS a configured price and must survive as a real row —
    /// the distinction NULL-vs-zero is the whole point.</summary>
    [Fact]
    public async Task Backfill_CreatesWholesaleRow_ForExplicitZero()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context, ("ZeroWholesale", "100", "0"));

        await context.Database.MigrateAsync();

        Assert.Equal(1, await ScalarAsync(context,
            "SELECT COUNT(*) FROM ProductPrices WHERE Level='Wholesale' AND IsActive=1"));
        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM ProductPrices pr
            JOIN Products p ON p.Id = pr.ProductId
            WHERE pr.Level='Wholesale' AND pr.Price <> p.WholesalePrice
            """));
    }

    [Fact]
    public async Task Backfill_CopiesConfiguredWholesalePricesExactly()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context,
            ("A", "100", "95"), ("B", "57.50", "49.99"), ("C", "10", null));

        await context.Database.MigrateAsync();

        Assert.Equal(2, await ScalarAsync(context,
            "SELECT COUNT(*) FROM ProductPrices WHERE Level='Wholesale' AND IsActive=1"));
        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM Products p
            JOIN ProductPrices pr ON pr.ProductId=p.Id AND pr.Level='Wholesale' AND pr.IsActive=1
            WHERE pr.Price <> p.WholesalePrice
            """));
    }

    /// <summary>
    /// The §5 fingerprint: describe every product's prices from the legacy columns before the
    /// migration and from ProductPrices after, and require the two descriptions to be identical.
    /// Catches a changed price, a skipped product, a duplicate, NULL becoming zero, and any change
    /// in decimal representation — in one assertion.
    /// </summary>
    [Fact]
    public async Task Backfill_PriceFingerprintIsUnchanged()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context,
            ("Alpha", "100", null),
            ("Beta", "57.50", "49.99"),
            ("Gamma", "0", "0"),
            ("Delta", "1234.56", null),
            ("Epsilon", "9.99", "9.99"));

        var before = await PriceFingerprintAsync(context, fromPrices: false);

        await context.Database.MigrateAsync();

        var after = await PriceFingerprintAsync(context, fromPrices: true);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Backfill_LeavesProjectionColumnsUntouched()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context, ("Alpha", "100", "95"), ("Beta", "57.50", null));

        var before = await PriceFingerprintAsync(context, fromPrices: false);
        await context.Database.MigrateAsync();
        var after = await PriceFingerprintAsync(context, fromPrices: false);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Backfill_CreatesNoOrphansOrDuplicates()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context, ("A", "10", "9"), ("B", "20", null), ("C", "30", "0"));

        await context.Database.MigrateAsync();

        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM ProductPrices pr
            LEFT JOIN Products p ON p.Id = pr.ProductId WHERE p.Id IS NULL
            """));
        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM (
                SELECT ProductId, Level FROM ProductPrices WHERE IsActive=1
                GROUP BY ProductId, Level HAVING COUNT(*) > 1)
            """));
    }

    /// <summary>Phase 15A is additive: a pricing migration must not disturb the transaction history
    /// or any Phase 13/14 data.</summary>
    [Fact]
    public async Task PricingMigration_LeavesProductCountAndHistoricalDataUntouched()
    {
        await using var context = CreateContext();
        await SeedPreP15ProductsAsync(context, ("Alpha", "100", "95"), ("Beta", "57.50", null));

        var connection = context.Database.GetDbConnection();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO StockMovements
                    (ProductId, MovementType, QuantityChange, PreviousQuantity, NewQuantity, TimestampUtc, CreatedAtUtc)
                VALUES (1, 'Purchase', 25, 0, 25, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                INSERT INTO Inventories (ProductId, QuantityOnHand, CreatedAtUtc)
                VALUES (1, 25, CURRENT_TIMESTAMP);
                """;
            await seed.ExecuteNonQueryAsync();
        }

        var productsBefore = await ScalarAsync(context, "SELECT COUNT(*) FROM Products");
        var movementsBefore = await ScalarAsync(context, "SELECT COUNT(*) FROM StockMovements");

        await context.Database.MigrateAsync();

        Assert.Equal(productsBefore, await ScalarAsync(context, "SELECT COUNT(*) FROM Products"));
        Assert.Equal(movementsBefore, await ScalarAsync(context, "SELECT COUNT(*) FROM StockMovements"));

        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT MovementType, QuantityChange, PreviousQuantity, NewQuantity FROM StockMovements";
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Purchase", reader.GetString(0));
        Assert.Equal(25m, reader.GetDecimal(1));
        Assert.Equal(0m, reader.GetDecimal(2));
        Assert.Equal(25m, reader.GetDecimal(3));

        Assert.Equal(25m, await ScalarAsync(context, "SELECT QuantityOnHand FROM Inventories"));
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

    // ---- Phase 15B-4 customer default price level ----

    /// <summary>The migration immediately before the customer column, so existing customer rows can
    /// be inserted the way the old schema stored them and then migrated forward.</summary>
    private const string PreCustomerPriceLevelMigration = "20260814141847_Phase15APricingFoundation";

    private async Task SeedPreCustomerPriceLevelAsync(KiranaDbContext context, params string[] names)
    {
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreCustomerPriceLevelMigration);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        for (var i = 0; i < names.Length; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Customers (CustomerCode, Name, Phone, CreditBalance, IsActive, CreatedAtUtc)
                VALUES ($code, $name, $phone, $balance, 1, CURRENT_TIMESTAMP);
                """;
            command.Parameters.Add(new SqliteParameter("$code", $"CUST-{i + 1:D6}"));
            command.Parameters.Add(new SqliteParameter("$name", names[i]));
            command.Parameters.Add(new SqliteParameter("$phone", $"90000000{i:D2}"));
            command.Parameters.Add(new SqliteParameter("$balance", 125.50m * (i + 1)));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task CustomersTable_GainsTheNullableDefaultPriceLevelColumn()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        Assert.Contains("DefaultPriceLevel", await ColumnNamesAsync(context, "Customers"));

        var connection = context.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT \"notnull\" FROM pragma_table_info('Customers') WHERE name='DefaultPriceLevel'";
        Assert.Equal(0L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));   // nullable
    }

    /// <summary>
    /// The point of the whole migration: existing customers must come through as NULL, meaning "no
    /// preference". Backfilling them to 'Retail' would invent a classification nobody made and
    /// would be indistinguishable from a real one afterwards.
    /// </summary>
    [Fact]
    public async Task ExistingCustomers_GetNoPreference_RatherThanADefaultedRetail()
    {
        await using var context = CreateContext();
        await SeedPreCustomerPriceLevelAsync(context, "Old Customer A", "Old Customer B");

        await context.Database.MigrateAsync();

        Assert.Equal(2, await ScalarAsync(context, "SELECT COUNT(*) FROM Customers"));
        Assert.Equal(2, await ScalarAsync(context,
            "SELECT COUNT(*) FROM Customers WHERE DefaultPriceLevel IS NULL"));
        Assert.Equal(0, await ScalarAsync(context,
            "SELECT COUNT(*) FROM Customers WHERE DefaultPriceLevel IS NOT NULL"));
    }

    /// <summary>Everything else about an existing customer survives — this is an added column, not
    /// a table rebuild that could drop or reorder data.</summary>
    [Fact]
    public async Task ExistingCustomerData_IsUnchangedByTheMigration()
    {
        await using var context = CreateContext();
        await SeedPreCustomerPriceLevelAsync(context, "Preserved Customer");

        var before = await CustomerFingerprintAsync(context);
        await context.Database.MigrateAsync();
        var after = await CustomerFingerprintAsync(context);

        Assert.Equal(before, after);
    }

    private static async Task<List<string>> CustomerFingerprintAsync(KiranaDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, CustomerCode, Name, Phone, CreditBalance, IsActive FROM Customers ORDER BY Id";
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var parts = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                parts.Add(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString()!);
            }

            rows.Add(string.Join("|", parts));
        }

        return rows;
    }

    /// <summary>The pricing tables this phase must not touch.</summary>
    [Fact]
    public async Task TheCustomerMigration_LeavesProductPricesAndSalesUntouched()
    {
        await using var context = CreateContext();
        await SeedPreCustomerPriceLevelAsync(context, "Customer With History");

        var connection = context.Database.GetDbConnection();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO Products
                    (ProductCode, Name, Unit, PurchasePrice, Mrp, SellingPrice, WholesalePrice,
                     MinimumStock, ReorderQuantity, TracksBatches, IsActive, CreatedAtUtc,
                     PricingType, GstRatePercent, ReplenishmentEnabled)
                VALUES ('PRD-000001', 'Priced', 'Piece', 10, 30, 25, 20, 0, 0, 0, 1,
                        CURRENT_TIMESTAMP, 'Inclusive', 5, 0);
                INSERT INTO ProductPrices (ProductId, Level, Price, IsActive, CreatedAtUtc)
                VALUES (1, 'Retail', 25, 1, CURRENT_TIMESTAMP),
                       (1, 'Wholesale', 20, 1, CURRENT_TIMESTAMP);
                """;
            await seed.ExecuteNonQueryAsync();
        }

        var pricesBefore = await ScalarAsync(context, "SELECT COUNT(*) FROM ProductPrices");

        await context.Database.MigrateAsync();

        Assert.Equal(pricesBefore, await ScalarAsync(context, "SELECT COUNT(*) FROM ProductPrices"));
        Assert.Equal(0, await ScalarAsync(context, """
            SELECT COUNT(*) FROM Products p
            JOIN ProductPrices pr ON pr.ProductId = p.Id AND pr.Level='Retail' AND pr.IsActive=1
            WHERE pr.Price <> p.SellingPrice
            """));
        Assert.Equal(0, await ScalarAsync(context, "SELECT COUNT(*) FROM Sales"));
    }

    // ---- Phase 15B-5 historical sale price level ----

    private const string PreSalePriceLevelMigration = "20260815131436_AddCustomerDefaultPriceLevel";

    /// <summary>Inserts sales the way the pre-15B-5 schema stored them — no price level at all — so
    /// migrating forward exercises the real backfill.</summary>
    private async Task SeedPreSalePriceLevelAsync(KiranaDbContext context, params (string Invoice, decimal Total)[] sales)
    {
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreSalePriceLevelMigration);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using (var product = connection.CreateCommand())
        {
            product.CommandText = """
                INSERT INTO Products
                    (ProductCode, Name, Unit, PurchasePrice, Mrp, SellingPrice, WholesalePrice,
                     MinimumStock, ReorderQuantity, TracksBatches, IsActive, CreatedAtUtc,
                     PricingType, GstRatePercent, ReplenishmentEnabled)
                VALUES ('PRD-000001', 'Historic', 'Piece', 10, 30, 25, 20, 0, 0, 0, 1,
                        CURRENT_TIMESTAMP, 'Inclusive', 5, 0);
                """;
            await product.ExecuteNonQueryAsync();
        }

        for (var i = 0; i < sales.Length; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Sales
                    (InvoiceNumber, SaleDateUtc, SubTotal, ItemDiscountTotal, PromotionDiscountTotal,
                     BillDiscountPercent, BillDiscountAmount, TaxableTotal, TaxTotal, RoundOffAmount,
                     GrandTotal, Status, CreatedAtUtc)
                VALUES ($invoice, CURRENT_TIMESTAMP, $total, 0, 0, 0, 0, $total, 0, 0, $total,
                        'Completed', CURRENT_TIMESTAMP);

                INSERT INTO SaleItems
                    (SaleId, ProductId, ProductNameSnapshot, ProductCodeSnapshot, UnitSnapshot,
                     IsTaxInclusiveSnapshot, GstRatePercentSnapshot, Quantity, UnitPriceSnapshot,
                     DiscountPercent, DiscountAmount, TaxableAmount, GstAmount, LineTotal,
                     MrpSnapshot, PromotionDiscountAmount, CreatedAtUtc)
                VALUES (last_insert_rowid(), 1, 'Historic', 'PRD-000001', 'Piece', 1, 0, 1, $total,
                        0, 0, $total, 0, $total, 30, 0, CURRENT_TIMESTAMP);
                """;
            command.Parameters.Add(new SqliteParameter("$invoice", sales[i].Invoice));
            command.Parameters.Add(new SqliteParameter("$total", sales[i].Total));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SalesTable_GainsANonNullPriceLevelColumn()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        Assert.Contains("PriceLevel", await ColumnNamesAsync(context, "Sales"));

        var connection = context.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT \"notnull\" FROM pragma_table_info('Sales') WHERE name='PriceLevel'";
        Assert.Equal(1L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));   // NOT NULL
    }

    /// <summary>
    /// The backfill policy: every pre-existing sale is labelled Retail. EF scaffolded this column
    /// with an empty-string default, which would have written a value that is not a member of the
    /// enum into every historical row — this asserts the corrected 'Retail'.
    /// </summary>
    [Fact]
    public async Task ExistingSales_AreBackfilledToRetail_NotToAnEmptyOrInvalidValue()
    {
        await using var context = CreateContext();
        await SeedPreSalePriceLevelAsync(context, ("INV-0001", 100m), ("INV-0002", 250m));

        await context.Database.MigrateAsync();

        Assert.Equal(2, await ScalarAsync(context, "SELECT COUNT(*) FROM Sales"));
        Assert.Equal(2, await ScalarAsync(context, "SELECT COUNT(*) FROM Sales WHERE PriceLevel='Retail'"));
        Assert.Equal(0, await ScalarAsync(context, "SELECT COUNT(*) FROM Sales WHERE PriceLevel='Wholesale'"));
        // No empty strings, nulls, or anything else that is not a level.
        Assert.Equal(0, await ScalarAsync(context,
            "SELECT COUNT(*) FROM Sales WHERE PriceLevel IS NULL OR PriceLevel NOT IN ('Retail','Wholesale')"));
    }

    /// <summary>Sales and their items must come through the migration untouched apart from the new
    /// column — no duplication, no lost rows, no altered money.</summary>
    [Fact]
    public async Task TheSaleMigration_LeavesSalesItemsAndTotalsUnchanged()
    {
        await using var context = CreateContext();
        await SeedPreSalePriceLevelAsync(context, ("INV-0001", 100m), ("INV-0002", 250m));

        var salesBefore = await SaleFingerprintAsync(context);
        var itemsBefore = await ScalarAsync(context, "SELECT COUNT(*) FROM SaleItems");
        var snapshotsBefore = await ScalarAsync(context,
            "SELECT COUNT(*) FROM SaleItems WHERE UnitPriceSnapshot IN (100, 250)");

        await context.Database.MigrateAsync();

        Assert.Equal(salesBefore, await SaleFingerprintAsync(context));
        Assert.Equal(itemsBefore, await ScalarAsync(context, "SELECT COUNT(*) FROM SaleItems"));
        Assert.Equal(snapshotsBefore, await ScalarAsync(context,
            "SELECT COUNT(*) FROM SaleItems WHERE UnitPriceSnapshot IN (100, 250)"));
        Assert.Equal(1, await ScalarAsync(context, "SELECT COUNT(*) FROM Products"));
        Assert.Equal(0, await ScalarAsync(context, "SELECT COUNT(*) FROM Payments"));
    }

    /// <summary>Invoice numbers, dates and money, described independently of the new column so the
    /// comparison is meaningful either side of the migration.</summary>
    private static async Task<List<string>> SaleFingerprintAsync(KiranaDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, InvoiceNumber, SaleDateUtc, SubTotal, TaxTotal, GrandTotal, Status
            FROM Sales ORDER BY Id
            """;
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var parts = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                parts.Add(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString()!);
            }

            rows.Add(string.Join("|", parts));
        }

        return rows;
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
