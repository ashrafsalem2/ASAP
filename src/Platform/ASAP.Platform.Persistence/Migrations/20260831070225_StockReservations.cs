using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockReservations",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VariantCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocumentLineNo = table.Column<int>(type: "int", nullable: true),
                    SourceCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    QuantityOutstanding = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_CompanyId_DocumentNo",
                schema: "inv",
                table: "StockReservations",
                columns: new[] { "CompanyId", "DocumentNo" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ItemId_VariantId_LocationId_QuantityOutstanding",
                schema: "inv",
                table: "StockReservations",
                columns: new[] { "ItemId", "VariantId", "LocationId", "QuantityOutstanding" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockReservations",
                schema: "inv");
        }
    }
}
