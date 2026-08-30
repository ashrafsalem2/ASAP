using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdjustmentReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                schema: "inv",
                table: "ItemLedgerEntries",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                schema: "inv",
                table: "ItemLedgerEntries",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdjustmentReasons",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContraAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    RequiresNote = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjustmentReasons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ReasonCode_PostingDate",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "ReasonCode", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AdjustmentReasons_CompanyId_Code",
                schema: "inv",
                table: "AdjustmentReasons",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdjustmentReasons",
                schema: "inv");

            migrationBuilder.DropIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ReasonCode_PostingDate",
                schema: "inv",
                table: "ItemLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Note",
                schema: "inv",
                table: "ItemLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                schema: "inv",
                table: "ItemLedgerEntries");
        }
    }
}
