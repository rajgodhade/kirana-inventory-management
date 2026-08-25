using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase18A1GstTaxIdentityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GstRegistrationType",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateCode",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GstRegistrationType",
                table: "Stores",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Stores",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateCode",
                table: "Stores",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GstRegistrationType",
                table: "Customers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateCode",
                table: "Customers",
                type: "TEXT",
                maxLength: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EF's SQLite provider rejects DropColumnOperation even though the SQLite version
            // shipped with the application supports ALTER TABLE ... DROP COLUMN. These columns
            // are nullable, unindexed foundation metadata, so direct DDL is safe and also keeps
            // the repository's migration-chain rollback rehearsal operational.
            migrationBuilder.Sql("ALTER TABLE Suppliers DROP COLUMN GstRegistrationType;");
            migrationBuilder.Sql("ALTER TABLE Suppliers DROP COLUMN StateCode;");
            migrationBuilder.Sql("ALTER TABLE Stores DROP COLUMN GstRegistrationType;");
            migrationBuilder.Sql("ALTER TABLE Stores DROP COLUMN LegalName;");
            migrationBuilder.Sql("ALTER TABLE Stores DROP COLUMN StateCode;");
            migrationBuilder.Sql("ALTER TABLE Customers DROP COLUMN GstRegistrationType;");
            migrationBuilder.Sql("ALTER TABLE Customers DROP COLUMN StateCode;");
        }
    }
}
