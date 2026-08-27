using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ControlAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TaxRegistrationNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ControlAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TaxRegistrationNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerLedgerEntries",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalDocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    ControlAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerLedgerEntries_Customers_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "fin",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorLedgerEntries",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalDocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    ControlAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorLedgerEntries_Vendors_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "fin",
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerApplications",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedFromEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedToEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    AppliedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerApplications_CustomerLedgerEntries_AppliedFromEntryId",
                        column: x => x.AppliedFromEntryId,
                        principalSchema: "fin",
                        principalTable: "CustomerLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerApplications_CustomerLedgerEntries_AppliedToEntryId",
                        column: x => x.AppliedToEntryId,
                        principalSchema: "fin",
                        principalTable: "CustomerLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorApplications",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedFromEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedToEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    AppliedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorApplications_VendorLedgerEntries_AppliedFromEntryId",
                        column: x => x.AppliedFromEntryId,
                        principalSchema: "fin",
                        principalTable: "VendorLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorApplications_VendorLedgerEntries_AppliedToEntryId",
                        column: x => x.AppliedToEntryId,
                        principalSchema: "fin",
                        principalTable: "VendorLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerApplications_AppliedFromEntryId",
                schema: "fin",
                table: "CustomerApplications",
                column: "AppliedFromEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerApplications_AppliedToEntryId",
                schema: "fin",
                table: "CustomerApplications",
                column: "AppliedToEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerApplications_CompanyId_AppliedFromEntryId",
                schema: "fin",
                table: "CustomerApplications",
                columns: new[] { "CompanyId", "AppliedFromEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerApplications_CompanyId_AppliedToEntryId",
                schema: "fin",
                table: "CustomerApplications",
                columns: new[] { "CompanyId", "AppliedToEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_CompanyId_PartyId_DueDate",
                schema: "fin",
                table: "CustomerLedgerEntries",
                columns: new[] { "CompanyId", "PartyId", "DueDate" },
                filter: "[IsOpen] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_CompanyId_PartyId_PostingDate",
                schema: "fin",
                table: "CustomerLedgerEntries",
                columns: new[] { "CompanyId", "PartyId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_CompanyId_TransactionNo",
                schema: "fin",
                table: "CustomerLedgerEntries",
                columns: new[] { "CompanyId", "TransactionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_PartyId",
                schema: "fin",
                table: "CustomerLedgerEntries",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_No",
                schema: "fin",
                table: "Customers",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_VendorApplications_AppliedFromEntryId",
                schema: "fin",
                table: "VendorApplications",
                column: "AppliedFromEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorApplications_AppliedToEntryId",
                schema: "fin",
                table: "VendorApplications",
                column: "AppliedToEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorApplications_CompanyId_AppliedFromEntryId",
                schema: "fin",
                table: "VendorApplications",
                columns: new[] { "CompanyId", "AppliedFromEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorApplications_CompanyId_AppliedToEntryId",
                schema: "fin",
                table: "VendorApplications",
                columns: new[] { "CompanyId", "AppliedToEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorLedgerEntries_CompanyId_PartyId_DueDate",
                schema: "fin",
                table: "VendorLedgerEntries",
                columns: new[] { "CompanyId", "PartyId", "DueDate" },
                filter: "[IsOpen] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_VendorLedgerEntries_CompanyId_PartyId_PostingDate",
                schema: "fin",
                table: "VendorLedgerEntries",
                columns: new[] { "CompanyId", "PartyId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorLedgerEntries_CompanyId_TransactionNo",
                schema: "fin",
                table: "VendorLedgerEntries",
                columns: new[] { "CompanyId", "TransactionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorLedgerEntries_PartyId",
                schema: "fin",
                table: "VendorLedgerEntries",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_CompanyId_No",
                schema: "fin",
                table: "Vendors",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerApplications",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "VendorApplications",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "CustomerLedgerEntries",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "VendorLedgerEntries",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "Vendors",
                schema: "fin");
        }
    }
}
