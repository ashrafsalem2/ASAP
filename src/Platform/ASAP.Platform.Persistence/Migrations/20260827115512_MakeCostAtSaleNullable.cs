using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeCostAtSaleNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every row that exists at this moment holds a zero because the column was not
            // nullable, not because anybody recorded that the goods cost nothing. Nothing has yet
            // written a deliberate zero, so this is safe exactly once -- here -- and it is what
            // stops the margin report answering "a hundred per cent" on the strength of no data.
            migrationBuilder.Sql(
                "UPDATE pos.PosReceiptLines SET UnitCostAtSale = NULL WHERE UnitCostAtSale = 0;");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCostAtSale",
                schema: "pos",
                table: "PosReceiptLines",
                type: "decimal(18,5)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCostAtSale",
                schema: "pos",
                table: "PosReceiptLines",
                type: "decimal(18,5)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,5)",
                oldNullable: true);
        }
    }
}
