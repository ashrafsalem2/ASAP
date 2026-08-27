using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCostAtSaleAndDropOfferCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimesApplied",
                schema: "prm",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "TotalGivenAway",
                schema: "prm",
                table: "Offers");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostAtSale",
                schema: "pos",
                table: "PosReceiptLines",
                type: "decimal(18,5)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitCostAtSale",
                schema: "pos",
                table: "PosReceiptLines");

            migrationBuilder.AddColumn<int>(
                name: "TimesApplied",
                schema: "prm",
                table: "Offers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalGivenAway",
                schema: "prm",
                table: "Offers",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
