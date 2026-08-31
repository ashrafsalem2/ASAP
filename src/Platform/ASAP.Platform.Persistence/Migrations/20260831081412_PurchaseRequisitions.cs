using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseRequisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseRequisitions",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequisitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NeededByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PurchaseRequisitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionLines",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseRequisitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VariantCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    EstimatedUnitCost = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    SuggestedVendorNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    QuantityOrdered = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseRequisitionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_PurchaseRequisitions_PurchaseRequisitionId",
                        column: x => x.PurchaseRequisitionId,
                        principalSchema: "pur",
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_PurchaseRequisitionId_LineNo",
                schema: "pur",
                table: "PurchaseRequisitionLines",
                columns: new[] { "PurchaseRequisitionId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_CompanyId_No",
                schema: "pur",
                table: "PurchaseRequisitions",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_CompanyId_Status",
                schema: "pur",
                table: "PurchaseRequisitions",
                columns: new[] { "CompanyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseRequisitionLines",
                schema: "pur");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitions",
                schema: "pur");
        }
    }
}
