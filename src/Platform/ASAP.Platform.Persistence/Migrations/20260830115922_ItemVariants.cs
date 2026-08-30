using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasVariants",
                schema: "inv",
                table: "Items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VariantCode",
                schema: "inv",
                table: "ItemLedgerEntries",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                schema: "inv",
                table: "ItemLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                schema: "inv",
                table: "ItemApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemVariants",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    DescriptionArabic = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_ItemVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemVariants_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inv",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_ItemId_VariantId_LocationId_RemainingQuantity",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "ItemId", "VariantId", "LocationId", "RemainingQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemApplications_ItemId_VariantId_IsOutstanding",
                schema: "inv",
                table: "ItemApplications",
                columns: new[] { "ItemId", "VariantId", "IsOutstanding" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_CompanyId_Barcode",
                schema: "inv",
                table: "ItemVariants",
                columns: new[] { "CompanyId", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_ItemId_Code",
                schema: "inv",
                table: "ItemVariants",
                columns: new[] { "ItemId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemVariants",
                schema: "inv");

            migrationBuilder.DropIndex(
                name: "IX_ItemLedgerEntries_ItemId_VariantId_LocationId_RemainingQuantity",
                schema: "inv",
                table: "ItemLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_ItemApplications_ItemId_VariantId_IsOutstanding",
                schema: "inv",
                table: "ItemApplications");

            migrationBuilder.DropColumn(
                name: "HasVariants",
                schema: "inv",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "VariantCode",
                schema: "inv",
                table: "ItemLedgerEntries");

            migrationBuilder.DropColumn(
                name: "VariantId",
                schema: "inv",
                table: "ItemLedgerEntries");

            migrationBuilder.DropColumn(
                name: "VariantId",
                schema: "inv",
                table: "ItemApplications");
        }
    }
}
