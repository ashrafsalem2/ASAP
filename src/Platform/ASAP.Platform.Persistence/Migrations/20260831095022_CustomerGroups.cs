using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerGroupCode",
                schema: "fin",
                table: "Vendors",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerGroupCode",
                schema: "fin",
                table: "Customers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerGroupPriceLists",
                schema: "sal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerGroupCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PriceListCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_CustomerGroupPriceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerGroups",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_CustomerGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_CompanyId_CustomerGroupCode",
                schema: "fin",
                table: "Vendors",
                columns: new[] { "CompanyId", "CustomerGroupCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_CustomerGroupCode",
                schema: "fin",
                table: "Customers",
                columns: new[] { "CompanyId", "CustomerGroupCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerGroupPriceLists_CompanyId_CustomerGroupCode",
                schema: "sal",
                table: "CustomerGroupPriceLists",
                columns: new[] { "CompanyId", "CustomerGroupCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerGroups_CompanyId_Code",
                schema: "fin",
                table: "CustomerGroups",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerGroupPriceLists",
                schema: "sal");

            migrationBuilder.DropTable(
                name: "CustomerGroups",
                schema: "fin");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_CompanyId_CustomerGroupCode",
                schema: "fin",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_CustomerGroupCode",
                schema: "fin",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomerGroupCode",
                schema: "fin",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "CustomerGroupCode",
                schema: "fin",
                table: "Customers");
        }
    }
}
