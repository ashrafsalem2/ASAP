using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterScheduleRowIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountScheduleLines_AccountScheduleId_RowNo",
                schema: "fin",
                table: "AccountScheduleLines");

            migrationBuilder.CreateIndex(
                name: "IX_AccountScheduleLines_AccountScheduleId_RowNo",
                schema: "fin",
                table: "AccountScheduleLines",
                columns: new[] { "AccountScheduleId", "RowNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountScheduleLines_AccountScheduleId_RowNo",
                schema: "fin",
                table: "AccountScheduleLines");

            migrationBuilder.CreateIndex(
                name: "IX_AccountScheduleLines_AccountScheduleId_RowNo",
                schema: "fin",
                table: "AccountScheduleLines",
                columns: new[] { "AccountScheduleId", "RowNo" },
                unique: true);
        }
    }
}
