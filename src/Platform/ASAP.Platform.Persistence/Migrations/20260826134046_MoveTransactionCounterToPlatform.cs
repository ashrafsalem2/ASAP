using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveTransactionCounterToPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "TransactionCounters",
                schema: "fin",
                newName: "TransactionCounters",
                newSchema: "asap");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "TransactionCounters",
                schema: "asap",
                newName: "TransactionCounters",
                newSchema: "fin");
        }
    }
}
