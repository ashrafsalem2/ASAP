using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prm");

            migrationBuilder.CreateTable(
                name: "Offers",
                schema: "prm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    BuyQuantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    GetQuantity = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    GetDiscountPercent = table.Column<decimal>(type: "decimal(9,5)", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    DaysOfWeek = table.Column<int>(type: "int", nullable: true),
                    Channels = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerGroup = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CouponCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Stacking = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TimesApplied = table.Column<int>(type: "int", nullable: false),
                    TotalGivenAway = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfferTargets",
                schema: "prm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferTargets_Offers_OfferId",
                        column: x => x.OfferId,
                        principalSchema: "prm",
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Offers_CompanyId_Code",
                schema: "prm",
                table: "Offers",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_CompanyId_IsActive_StartsOn_EndsOn",
                schema: "prm",
                table: "Offers",
                columns: new[] { "CompanyId", "IsActive", "StartsOn", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferTargets_OfferId_CategoryId",
                schema: "prm",
                table: "OfferTargets",
                columns: new[] { "OfferId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferTargets_OfferId_ItemNo",
                schema: "prm",
                table: "OfferTargets",
                columns: new[] { "OfferId", "ItemNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfferTargets",
                schema: "prm");

            migrationBuilder.DropTable(
                name: "Offers",
                schema: "prm");
        }
    }
}
