using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosLineUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuantityPerUnit",
                schema: "pos",
                table: "PosReceiptLines",
                type: "decimal(18,5)",
                nullable: false,

                // One, not nought. Every line already in the database was rung in the base unit,
                // and a factor of nought would say a case held nothing.
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "UnitCode",
                schema: "pos",
                table: "PosReceiptLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuantityPerUnit",
                schema: "pos",
                table: "PosReceiptLines");

            migrationBuilder.DropColumn(
                name: "UnitCode",
                schema: "pos",
                table: "PosReceiptLines");
        }
    }
}
