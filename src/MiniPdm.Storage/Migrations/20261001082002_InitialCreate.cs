using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniPdm.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bom_links",
                columns: table => new
                {
                    ParentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_links", x => new { x.ParentVersionId, x.ChildObjectId });
                    table.CheckConstraint("ck_bom_links_quantity_positive", "\"Quantity\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "object_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Material = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Mass = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_versions", x => x.Id);
                    table.UniqueConstraint("AK_object_versions_Id_ObjectId", x => new { x.Id, x.ObjectId });
                    table.CheckConstraint("ck_object_versions_mass_nonnegative", "\"Mass\" IS NULL OR \"Mass\" >= 0");
                    table.CheckConstraint("ck_object_versions_state", "\"State\" IN (1, 2, 3)");
                    table.CheckConstraint("ck_object_versions_version_positive", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "pdm_objects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Designation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pdm_objects", x => x.Id);
                    table.CheckConstraint("ck_pdm_objects_identity", "(\"Type\" IN (1,2) AND \"Designation\" IS NOT NULL AND \"NormalizedName\" IS NULL) OR (\"Type\" = 3 AND \"Designation\" IS NULL AND \"NormalizedName\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_pdm_objects_object_versions_CurrentVersionId_Id",
                        columns: x => new { x.CurrentVersionId, x.Id },
                        principalTable: "object_versions",
                        principalColumns: new[] { "Id", "ObjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bom_links_ChildObjectId",
                table: "bom_links",
                column: "ChildObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_object_versions_ObjectId_Version",
                table: "object_versions",
                columns: new[] { "ObjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pdm_objects_CurrentVersionId_Id",
                table: "pdm_objects",
                columns: new[] { "CurrentVersionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_pdm_objects_Designation",
                table: "pdm_objects",
                column: "Designation",
                unique: true,
                filter: "\"Type\" IN (1, 2) AND \"Designation\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pdm_objects_NormalizedName",
                table: "pdm_objects",
                column: "NormalizedName",
                unique: true,
                filter: "\"Type\" = 3 AND \"NormalizedName\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_bom_links_object_versions_ParentVersionId",
                table: "bom_links",
                column: "ParentVersionId",
                principalTable: "object_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_bom_links_pdm_objects_ChildObjectId",
                table: "bom_links",
                column: "ChildObjectId",
                principalTable: "pdm_objects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_object_versions_pdm_objects_ObjectId",
                table: "object_versions",
                column: "ObjectId",
                principalTable: "pdm_objects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pdm_objects_object_versions_CurrentVersionId_Id",
                table: "pdm_objects");

            migrationBuilder.DropTable(
                name: "bom_links");

            migrationBuilder.DropTable(
                name: "object_versions");

            migrationBuilder.DropTable(
                name: "pdm_objects");
        }
    }
}
