using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniPdm.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJournalAndStandardName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StandardName",
                table: "pdm_objects",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_journal",
                columns: table => new
                {
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_journal", x => x.ImportId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_journal");

            migrationBuilder.DropColumn(
                name: "StandardName",
                table: "pdm_objects");
        }
    }
}
