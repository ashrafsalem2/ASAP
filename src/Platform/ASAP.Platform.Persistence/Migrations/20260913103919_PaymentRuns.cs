using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankName",
                schema: "fin",
                table: "Vendors",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bic",
                schema: "fin",
                table: "Vendors",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                schema: "fin",
                table: "Vendors",
                type: "nvarchar(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bic",
                schema: "fin",
                table: "BankAccounts",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentRuns",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    BankAccountCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueByDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ExportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExportedByUserName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    FileContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FileSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_PaymentRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRunLines",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    VendorNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VendorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    Bic = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_PaymentRunLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRunLines_PaymentRuns_PaymentRunId",
                        column: x => x.PaymentRunId,
                        principalSchema: "fin",
                        principalTable: "PaymentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRunInvoices",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentRunLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_PaymentRunInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRunInvoices_PaymentRunLines_PaymentRunLineId",
                        column: x => x.PaymentRunLineId,
                        principalSchema: "fin",
                        principalTable: "PaymentRunLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRunInvoices_CompanyId_VendorLedgerEntryId",
                schema: "fin",
                table: "PaymentRunInvoices",
                columns: new[] { "CompanyId", "VendorLedgerEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRunInvoices_PaymentRunLineId",
                schema: "fin",
                table: "PaymentRunInvoices",
                column: "PaymentRunLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRunLines_PaymentRunId",
                schema: "fin",
                table: "PaymentRunLines",
                column: "PaymentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRuns_CompanyId_No",
                schema: "fin",
                table: "PaymentRuns",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRunInvoices",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "PaymentRunLines",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "PaymentRuns",
                schema: "fin");

            migrationBuilder.DropColumn(
                name: "BankName",
                schema: "fin",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Bic",
                schema: "fin",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Iban",
                schema: "fin",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Bic",
                schema: "fin",
                table: "BankAccounts");
        }
    }
}
