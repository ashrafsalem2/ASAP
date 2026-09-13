using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransferLineUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferOrderLineUnits",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackingNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QuantityShipped = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
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
                    table.PrimaryKey("PK_TransferOrderLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferOrderLineUnits_TransferOrderLines_TransferOrderLineId",
                        column: x => x.TransferOrderLineId,
                        principalSchema: "inv",
                        principalTable: "TransferOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLineUnits_TransferOrderLineId_TrackingNo",
                schema: "inv",
                table: "TransferOrderLineUnits",
                columns: new[] { "TransferOrderLineId", "TrackingNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferOrderLineUnits",
                schema: "inv");
        }
    }
}
