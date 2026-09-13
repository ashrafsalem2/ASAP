using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ItemId_LotNo",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "ItemId", "LotNo" },
                filter: "[LotNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ItemId_SerialNo",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "ItemId", "SerialNo" },
                filter: "[SerialNo] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ItemId_LotNo",
                schema: "inv",
                table: "ItemLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ItemId_SerialNo",
                schema: "inv",
                table: "ItemLedgerEntries");
        }
    }
}
