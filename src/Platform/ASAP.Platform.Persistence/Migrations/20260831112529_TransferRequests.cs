using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransferRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferRequests",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromLocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToLocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NeededByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUserName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
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
                    table.PrimaryKey("PK_TransferRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransferRequestLines",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VariantCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    QuantityRequested = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    QuantityApproved = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    QuantityTransferred = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
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
                    table.PrimaryKey("PK_TransferRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferRequestLines_TransferRequests_TransferRequestId",
                        column: x => x.TransferRequestId,
                        principalSchema: "inv",
                        principalTable: "TransferRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequestLines_CompanyId_TransferRequestId_LineNo",
                schema: "inv",
                table: "TransferRequestLines",
                columns: new[] { "CompanyId", "TransferRequestId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequestLines_TransferRequestId",
                schema: "inv",
                table: "TransferRequestLines",
                column: "TransferRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_CompanyId_No",
                schema: "inv",
                table: "TransferRequests",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_CompanyId_Status_RequestDate",
                schema: "inv",
                table: "TransferRequests",
                columns: new[] { "CompanyId", "Status", "RequestDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferRequestLines",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "TransferRequests",
                schema: "inv");
        }
    }
}
