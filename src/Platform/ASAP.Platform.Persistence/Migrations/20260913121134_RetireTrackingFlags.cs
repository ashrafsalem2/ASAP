using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireTrackingFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Carried across before the flags go. They were never read, but a value somebody set by
            // hand should land somewhere rather than vanish; serial wins where both were set, since
            // "both" was never a state that meant anything.
            migrationBuilder.Sql("UPDATE [inv].[Items] SET [Tracking] = 1 WHERE [IsLotTracked] = 1 AND [Tracking] = 0;");
            migrationBuilder.Sql("UPDATE [inv].[Items] SET [Tracking] = 2 WHERE [IsSerialTracked] = 1;");

            migrationBuilder.DropColumn(
                name: "IsLotTracked",
                schema: "inv",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IsSerialTracked",
                schema: "inv",
                table: "Items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLotTracked",
                schema: "inv",
                table: "Items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSerialTracked",
                schema: "inv",
                table: "Items",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
