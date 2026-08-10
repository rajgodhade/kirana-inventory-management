using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KiranaDbContext))]
[Migration("20260811090000_AddCloudBackupSettings")]
public partial class AddCloudBackupSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "CloudBackupProvider", table: "AppSettings", type: "TEXT", nullable: false, defaultValue: "None");
        migrationBuilder.AddColumn<bool>(name: "CloudAutomaticBackupEnabled", table: "AppSettings", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "CloudBackupFrequency", table: "AppSettings", type: "TEXT", nullable: false, defaultValue: "Daily");
        migrationBuilder.AddColumn<string>(name: "CloudBackupTime", table: "AppSettings", type: "TEXT", nullable: false, defaultValue: "23:00");
        migrationBuilder.AddColumn<int>(name: "CloudBackupRetentionCount", table: "AppSettings", type: "INTEGER", nullable: false, defaultValue: 30);
        migrationBuilder.AddColumn<DateTime>(name: "LastCloudBackupUtc", table: "AppSettings", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CloudBackupProvider", table: "AppSettings");
        migrationBuilder.DropColumn(name: "CloudAutomaticBackupEnabled", table: "AppSettings");
        migrationBuilder.DropColumn(name: "CloudBackupFrequency", table: "AppSettings");
        migrationBuilder.DropColumn(name: "CloudBackupTime", table: "AppSettings");
        migrationBuilder.DropColumn(name: "CloudBackupRetentionCount", table: "AppSettings");
        migrationBuilder.DropColumn(name: "LastCloudBackupUtc", table: "AppSettings");
    }
}
