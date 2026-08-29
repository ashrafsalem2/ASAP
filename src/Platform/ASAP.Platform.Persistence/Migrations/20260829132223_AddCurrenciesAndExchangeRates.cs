using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrenciesAndExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountInCurrency",
                schema: "fin",
                table: "VendorLedgerEntries",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmountInCurrency",
                schema: "fin",
                table: "VendorLedgerEntries",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountInCurrency",
                schema: "fin",
                table: "VendorApplications",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeDifference",
                schema: "fin",
                table: "VendorApplications",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "ExchangeTransactionNo",
                schema: "fin",
                table: "VendorApplications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FromBaseAmount",
                schema: "fin",
                table: "VendorApplications",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ToBaseAmount",
                schema: "fin",
                table: "VendorApplications",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountInCurrency",
                schema: "fin",
                table: "CustomerLedgerEntries",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmountInCurrency",
                schema: "fin",
                table: "CustomerLedgerEntries",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountInCurrency",
                schema: "fin",
                table: "CustomerApplications",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeDifference",
                schema: "fin",
                table: "CustomerApplications",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "ExchangeTransactionNo",
                schema: "fin",
                table: "CustomerApplications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FromBaseAmount",
                schema: "fin",
                table: "CustomerApplications",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ToBaseAmount",
                schema: "fin",
                table: "CustomerApplications",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "fin",
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_CompanyId_Code",
                schema: "fin",
                table: "Currencies",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyId_StartingDate",
                schema: "fin",
                table: "ExchangeRates",
                columns: new[] { "CurrencyId", "StartingDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "fin");

            migrationBuilder.DropColumn(
                name: "AmountInCurrency",
                schema: "fin",
                table: "VendorLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RemainingAmountInCurrency",
                schema: "fin",
                table: "VendorLedgerEntries");

            migrationBuilder.DropColumn(
                name: "AmountInCurrency",
                schema: "fin",
                table: "VendorApplications");

            migrationBuilder.DropColumn(
                name: "ExchangeDifference",
                schema: "fin",
                table: "VendorApplications");

            migrationBuilder.DropColumn(
                name: "ExchangeTransactionNo",
                schema: "fin",
                table: "VendorApplications");

            migrationBuilder.DropColumn(
                name: "FromBaseAmount",
                schema: "fin",
                table: "VendorApplications");

            migrationBuilder.DropColumn(
                name: "ToBaseAmount",
                schema: "fin",
                table: "VendorApplications");

            migrationBuilder.DropColumn(
                name: "AmountInCurrency",
                schema: "fin",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RemainingAmountInCurrency",
                schema: "fin",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropColumn(
                name: "AmountInCurrency",
                schema: "fin",
                table: "CustomerApplications");

            migrationBuilder.DropColumn(
                name: "ExchangeDifference",
                schema: "fin",
                table: "CustomerApplications");

            migrationBuilder.DropColumn(
                name: "ExchangeTransactionNo",
                schema: "fin",
                table: "CustomerApplications");

            migrationBuilder.DropColumn(
                name: "FromBaseAmount",
                schema: "fin",
                table: "CustomerApplications");

            migrationBuilder.DropColumn(
                name: "ToBaseAmount",
                schema: "fin",
                table: "CustomerApplications");
        }
    }
}
