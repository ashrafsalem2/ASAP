using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedAmount",
                schema: "pur",
                table: "PurchaseOrders",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                schema: "pur",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                schema: "pur",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserName",
                schema: "pur",
                table: "PurchaseOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RaisedByUserId",
                schema: "pur",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "pur",
                table: "PurchaseOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseApprovalLimits",
                schema: "pur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MaximumAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseApprovalLimits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseApprovalLimits_CompanyId_UserId",
                schema: "pur",
                table: "PurchaseApprovalLimits",
                columns: new[] { "CompanyId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseApprovalLimits",
                schema: "pur");

            migrationBuilder.DropColumn(
                name: "ApprovedAmount",
                schema: "pur",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                schema: "pur",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "pur",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserName",
                schema: "pur",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RaisedByUserId",
                schema: "pur",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "pur",
                table: "PurchaseOrders");
        }
    }
}
