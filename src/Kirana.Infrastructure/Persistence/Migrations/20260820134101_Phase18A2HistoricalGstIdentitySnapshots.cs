using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase18A2HistoricalGstIdentitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerAddressSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerGstRegistrationTypeSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerGstinSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhoneSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerStateCodeSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerStateNameSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GstIdentitySnapshotCapturedAtUtc",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreAddressSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreCitySnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreContactNumberSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreGstRegistrationTypeSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreGstinSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreLegalNameSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorePinCodeSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreStateCodeSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreStateNameSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreTradeNameSnapshot",
                table: "Sales",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GstIdentitySnapshotCapturedAtUtc",
                table: "Purchases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreAddressSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreCitySnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreContactNumberSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreGstRegistrationTypeSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreGstinSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreLegalNameSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorePinCodeSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreStateCodeSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreStateNameSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreTradeNameSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierAddressSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierCodeSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierGstRegistrationTypeSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierGstinSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNameSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierStateCodeSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierStateNameSnapshot",
                table: "Purchases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EF's SQLite rebuild path can fail on this project's rich FK graph. Modern SQLite
            // supports DROP COLUMN directly, and the rehearsal explicitly exercises this Down path.
            foreach (var column in SaleSnapshotColumns)
                migrationBuilder.Sql($"ALTER TABLE \"Sales\" DROP COLUMN \"{column}\";");

            foreach (var column in PurchaseSnapshotColumns)
                migrationBuilder.Sql($"ALTER TABLE \"Purchases\" DROP COLUMN \"{column}\";");
        }

        private static readonly string[] SaleSnapshotColumns =
        [
            "CustomerAddressSnapshot", "CustomerGstRegistrationTypeSnapshot", "CustomerGstinSnapshot",
            "CustomerNameSnapshot", "CustomerPhoneSnapshot", "CustomerStateCodeSnapshot",
            "CustomerStateNameSnapshot", "GstIdentitySnapshotCapturedAtUtc", "StoreAddressSnapshot",
            "StoreCitySnapshot", "StoreContactNumberSnapshot", "StoreGstRegistrationTypeSnapshot",
            "StoreGstinSnapshot", "StoreLegalNameSnapshot", "StorePinCodeSnapshot",
            "StoreStateCodeSnapshot", "StoreStateNameSnapshot", "StoreTradeNameSnapshot",
        ];

        private static readonly string[] PurchaseSnapshotColumns =
        [
            "GstIdentitySnapshotCapturedAtUtc", "StoreAddressSnapshot", "StoreCitySnapshot",
            "StoreContactNumberSnapshot", "StoreGstRegistrationTypeSnapshot", "StoreGstinSnapshot",
            "StoreLegalNameSnapshot", "StorePinCodeSnapshot", "StoreStateCodeSnapshot",
            "StoreStateNameSnapshot", "StoreTradeNameSnapshot", "SupplierAddressSnapshot",
            "SupplierCodeSnapshot", "SupplierGstRegistrationTypeSnapshot", "SupplierGstinSnapshot",
            "SupplierNameSnapshot", "SupplierStateCodeSnapshot", "SupplierStateNameSnapshot",
        ];
    }
}
