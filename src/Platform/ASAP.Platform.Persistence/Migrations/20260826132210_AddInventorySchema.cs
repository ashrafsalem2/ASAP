using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inv");

            migrationBuilder.CreateTable(
                name: "ItemApplications",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboundEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboundEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsOutstanding = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemCategories",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InventoryAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CostOfGoodsSoldAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SalesAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VarianceAccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_ItemCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemLedgerEntries",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    IsApplied = table.Column<bool>(type: "bit", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DimensionSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WentNegative = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSellable = table.Column<bool>(type: "bit", nullable: false),
                    IsInTransit = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    DescriptionArabic = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaseUnitOfMeasure = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CostingMethod = table.Column<int>(type: "int", nullable: false),
                    StandardCost = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    LastDirectCost = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReorderPoint = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    AllowNegativeInventory = table.Column<bool>(type: "bit", nullable: true),
                    IsSerialTracked = table.Column<bool>(type: "bit", nullable: false),
                    IsLotTracked = table.Column<bool>(type: "bit", nullable: false),
                    HasLedgerEntries = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_ItemCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "inv",
                        principalTable: "ItemCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValueEntries",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    ItemLedgerEntryType = table.Column<int>(type: "int", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    SalesAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    IsExpected = table.Column<bool>(type: "bit", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    GlTransactionNo = table.Column<long>(type: "bigint", nullable: true),
                    IsPostedToGl = table.Column<bool>(type: "bit", nullable: false),
                    DimensionSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValueEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValueEntries_ItemLedgerEntries_ItemLedgerEntryId",
                        column: x => x.ItemLedgerEntryId,
                        principalSchema: "inv",
                        principalTable: "ItemLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemApplications_InboundEntryId",
                schema: "inv",
                table: "ItemApplications",
                column: "InboundEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemApplications_OutboundEntryId",
                schema: "inv",
                table: "ItemApplications",
                column: "OutboundEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemApplications_Outstanding",
                schema: "inv",
                table: "ItemApplications",
                columns: new[] { "CompanyId", "ItemId" },
                filter: "[IsOutstanding] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategories_CompanyId_Code",
                schema: "inv",
                table: "ItemCategories",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_CompanyId_ItemId_LocationId_PostingDate",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "ItemId", "LocationId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_CompanyId_TransactionNo",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "TransactionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_OpenLayers",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "ItemId", "LocationId", "PostingDate", "EntryType" },
                filter: "[RemainingQuantity] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ItemLedgerEntries_WentNegative",
                schema: "inv",
                table: "ItemLedgerEntries",
                columns: new[] { "CompanyId", "ItemId" },
                filter: "[WentNegative] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CategoryId",
                schema: "inv",
                table: "Items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_Barcode",
                schema: "inv",
                table: "Items",
                columns: new[] { "CompanyId", "Barcode" },
                filter: "[Barcode] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_No",
                schema: "inv",
                table: "Items",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CompanyId_Code",
                schema: "inv",
                table: "Locations",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ValueEntries_AwaitingGl",
                schema: "inv",
                table: "ValueEntries",
                columns: new[] { "CompanyId", "IsPostedToGl" },
                filter: "[IsPostedToGl] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ValueEntries_CompanyId_ItemId_PostingDate",
                schema: "inv",
                table: "ValueEntries",
                columns: new[] { "CompanyId", "ItemId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueEntries_ItemLedgerEntryId",
                schema: "inv",
                table: "ValueEntries",
                column: "ItemLedgerEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemApplications",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "Items",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "Locations",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ValueEntries",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ItemCategories",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ItemLedgerEntries",
                schema: "inv");
        }
    }
}
