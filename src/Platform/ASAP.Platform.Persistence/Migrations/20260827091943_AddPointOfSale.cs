using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPointOfSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos");

            migrationBuilder.CreateTable(
                name: "PosSessions",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CashierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CashierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CashTendered = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ChangeGiven = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CashRefunded = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CardTaken = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    OnAccountTaken = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    NetSales = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ReceiptCount = table.Column<int>(type: "int", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeclaredCash = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ReadingCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosingTransactionNo = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PosStations",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultCustomerNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PosReceipts",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TakenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ParkedAs = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnsReceiptNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RoundingAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ChangeGiven = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: true),
                    CashierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosReceipts_PosSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "pos",
                        principalTable: "PosSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosReceiptLines",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(9,5)", nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosReceiptLines_PosReceipts_PosReceiptId",
                        column: x => x.PosReceiptId,
                        principalSchema: "pos",
                        principalTable: "PosReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PosTenders",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosTenders_PosReceipts_PosReceiptId",
                        column: x => x.PosReceiptId,
                        principalSchema: "pos",
                        principalTable: "PosReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosReceiptLines_PosReceiptId_LineNo",
                schema: "pos",
                table: "PosReceiptLines",
                columns: new[] { "PosReceiptId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosReceipts_CompanyId_No",
                schema: "pos",
                table: "PosReceipts",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PosReceipts_CompanyId_StationCode_BusinessDate",
                schema: "pos",
                table: "PosReceipts",
                columns: new[] { "CompanyId", "StationCode", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PosReceipts_SessionId_Status",
                schema: "pos",
                table: "PosReceipts",
                columns: new[] { "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_CompanyId_BusinessDate",
                schema: "pos",
                table: "PosSessions",
                columns: new[] { "CompanyId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_CompanyId_No",
                schema: "pos",
                table: "PosSessions",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_CompanyId_StationCode_Status",
                schema: "pos",
                table: "PosSessions",
                columns: new[] { "CompanyId", "StationCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PosStations_CompanyId_Code",
                schema: "pos",
                table: "PosStations",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PosTenders_PosReceiptId_LineNo",
                schema: "pos",
                table: "PosTenders",
                columns: new[] { "PosReceiptId", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosReceiptLines",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosStations",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosTenders",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosReceipts",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosSessions",
                schema: "pos");
        }
    }
}
