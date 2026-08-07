using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHardwareIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoFocusScannerInput",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoPrintReceipt",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BarcodeScannerEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPrinterName",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableSoundOnScan",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoicePrinterName",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHardwareMaintenanceUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OpenCashDrawerAfterCashPayment",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrintDuplicateCopy",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptPaperSize",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptPrinterName",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScannerTimeoutMilliseconds",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 40);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoFocusScannerInput",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "AutoPrintReceipt",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeScannerEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPrinterName",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EnableSoundOnScan",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "InvoicePrinterName",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LastHardwareMaintenanceUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "OpenCashDrawerAfterCashPayment",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrintDuplicateCopy",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptPaperSize",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptPrinterName",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ScannerTimeoutMilliseconds",
                table: "AppSettings");
        }
    }
}
