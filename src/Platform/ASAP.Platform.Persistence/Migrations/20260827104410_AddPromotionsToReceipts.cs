using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionsToReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PromotionAmount",
                schema: "pos",
                table: "PosReceipts",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OfferCode",
                schema: "pos",
                table: "PosReceiptLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OfferDiscountAmount",
                schema: "pos",
                table: "PosReceiptLines",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_PosReceiptLines_CompanyId_OfferCode",
                schema: "pos",
                table: "PosReceiptLines",
                columns: new[] { "CompanyId", "OfferCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PosReceiptLines_CompanyId_OfferCode",
                schema: "pos",
                table: "PosReceiptLines");

            migrationBuilder.DropColumn(
                name: "PromotionAmount",
                schema: "pos",
                table: "PosReceipts");

            migrationBuilder.DropColumn(
                name: "OfferCode",
                schema: "pos",
                table: "PosReceiptLines");

            migrationBuilder.DropColumn(
                name: "OfferDiscountAmount",
                schema: "pos",
                table: "PosReceiptLines");
        }
    }
}
