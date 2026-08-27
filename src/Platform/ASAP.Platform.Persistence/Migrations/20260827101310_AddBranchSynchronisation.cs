using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ASAP.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchSynchronisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BranchSyncState",
                schema: "asap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastAppliedSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastPulledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastPushedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentsPushed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchSyncState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncChanges",
                schema: "asap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncInbox",
                schema: "asap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsApplied = table.Column<bool>(type: "bit", nullable: false),
                    HeldReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncInbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BranchSyncState_CompanyId_BranchId",
                schema: "asap",
                table: "BranchSyncState",
                columns: new[] { "CompanyId", "BranchId" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_CompanyId_Sequence",
                schema: "asap",
                table: "SyncChanges",
                columns: new[] { "CompanyId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncInbox_CompanyId_BranchId_IdempotencyKey",
                schema: "asap",
                table: "SyncInbox",
                columns: new[] { "CompanyId", "BranchId", "IdempotencyKey" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchSyncState",
                schema: "asap");

            migrationBuilder.DropTable(
                name: "SyncChanges",
                schema: "asap");

            migrationBuilder.DropTable(
                name: "SyncInbox",
                schema: "asap");
        }
    }
}
