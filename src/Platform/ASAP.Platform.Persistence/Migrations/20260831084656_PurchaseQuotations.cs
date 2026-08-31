using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseQuotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseQuotationRequests",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RespondByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NeededByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RequisitionNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PurchaseQuotationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseQuotationInvitations",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseQuotationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VendorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeclinedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PurchaseQuotationInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseQuotationInvitations_PurchaseQuotationRequests_PurchaseQuotationRequestId",
                        column: x => x.PurchaseQuotationRequestId,
                        principalSchema: "pur",
                        principalTable: "PurchaseQuotationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseQuotationRequestLines",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseQuotationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VariantCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    AwardedVendorNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AwardedUnitCost = table.Column<decimal>(type: "decimal(18,5)", nullable: true),
                    AwardReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AwardedOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_PurchaseQuotationRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseQuotationRequestLines_PurchaseQuotationRequests_PurchaseQuotationRequestId",
                        column: x => x.PurchaseQuotationRequestId,
                        principalSchema: "pur",
                        principalTable: "PurchaseQuotationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseQuotationResponses",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseQuotationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    VendorNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LineAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseQuotationResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseQuotationResponses_PurchaseQuotationRequests_PurchaseQuotationRequestId",
                        column: x => x.PurchaseQuotationRequestId,
                        principalSchema: "pur",
                        principalTable: "PurchaseQuotationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseQuotationInvitations_PurchaseQuotationRequestId_VendorNo",
                schema: "pur",
                table: "PurchaseQuotationInvitations",
                columns: new[] { "PurchaseQuotationRequestId", "VendorNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseQuotationRequestLines_PurchaseQuotationRequestId_LineNo",
                schema: "pur",
                table: "PurchaseQuotationRequestLines",
                columns: new[] { "PurchaseQuotationRequestId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseQuotationRequests_CompanyId_No",
                schema: "pur",
                table: "PurchaseQuotationRequests",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseQuotationResponses_PurchaseQuotationRequestId_LineNo_VendorNo",
                schema: "pur",
                table: "PurchaseQuotationResponses",
                columns: new[] { "PurchaseQuotationRequestId", "LineNo", "VendorNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseQuotationInvitations",
                schema: "pur");

            migrationBuilder.DropTable(
                name: "PurchaseQuotationRequestLines",
                schema: "pur");

            migrationBuilder.DropTable(
                name: "PurchaseQuotationResponses",
                schema: "pur");

            migrationBuilder.DropTable(
                name: "PurchaseQuotationRequests",
                schema: "pur");
        }
    }
}
