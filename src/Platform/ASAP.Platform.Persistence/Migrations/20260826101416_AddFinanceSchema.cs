using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fin");

            migrationBuilder.CreateTable(
                name: "FiscalYears",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncomeTransferred = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_FiscalYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlAccounts",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Totaling = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Indentation = table.Column<int>(type: "int", nullable: false),
                    AllowsDirectPosting = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    RequiresDimensions = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_GlAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalBatches",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NumberSeriesCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DefaultBalancingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_JournalBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionCounters",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastTransactionNo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodNo = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    IsAdjustment = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalPeriods_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "fin",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlEntries",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    AmountInCurrency = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    DimensionSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShortcutDimension1Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShortcutDimension2Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false),
                    ReversedByEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversalOfEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlEntries_GlAccounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "fin",
                        principalTable: "GlAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    BalancingAccountType = table.Column<int>(type: "int", nullable: true),
                    BalancingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DimensionSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliesToEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalDocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "fin",
                        principalTable: "JournalBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_CompanyId_StartDate_EndDate",
                schema: "fin",
                table: "FiscalPeriods",
                columns: new[] { "CompanyId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_FiscalYearId_PeriodNo",
                schema: "fin",
                table: "FiscalPeriods",
                columns: new[] { "FiscalYearId", "PeriodNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_CompanyId_Code",
                schema: "fin",
                table: "FiscalYears",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_CompanyId_StartDate_EndDate",
                schema: "fin",
                table: "FiscalYears",
                columns: new[] { "CompanyId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GlAccounts_CompanyId_No",
                schema: "fin",
                table: "GlAccounts",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_AccountId",
                schema: "fin",
                table: "GlEntries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_CompanyId_AccountId_PostingDate",
                schema: "fin",
                table: "GlEntries",
                columns: new[] { "CompanyId", "AccountId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_CompanyId_DocumentNo",
                schema: "fin",
                table: "GlEntries",
                columns: new[] { "CompanyId", "DocumentNo" },
                filter: "[DocumentNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_CompanyId_ShortcutDimension1Id_PostingDate",
                schema: "fin",
                table: "GlEntries",
                columns: new[] { "CompanyId", "ShortcutDimension1Id", "PostingDate" },
                filter: "[ShortcutDimension1Id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_CompanyId_TransactionNo",
                schema: "fin",
                table: "GlEntries",
                columns: new[] { "CompanyId", "TransactionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalBatches_CompanyId_Code",
                schema: "fin",
                table: "JournalBatches",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_BatchId_LineNo",
                schema: "fin",
                table: "JournalLines",
                columns: new[] { "BatchId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCounters_CompanyId",
                schema: "fin",
                table: "TransactionCounters",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalPeriods",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "GlEntries",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "JournalLines",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "TransactionCounters",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "FiscalYears",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "GlAccounts",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "JournalBatches",
                schema: "fin");
        }
    }
}
