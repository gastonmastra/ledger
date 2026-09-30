using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Debts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EntryId",
                schema: "debts",
                table: "operations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsDisputed",
                schema: "debts",
                table: "operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingChangeId",
                schema: "debts",
                table: "operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesOperationId",
                schema: "debts",
                table: "operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "debts",
                table: "operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WalletId",
                schema: "debts",
                table: "operations",
                type: "uuid",
                nullable: true);

            // Existing operations are the first version of themselves.
            migrationBuilder.Sql("""
                UPDATE debts.operations SET "EntryId" = "Id", "Revision" = 1;

                UPDATE debts.operations AS o
                SET "WalletId" = p."WalletId"
                FROM debts.operation_payers AS p
                WHERE p."OperationId" = o."Id" AND o."Kind" = 'Settlement' AND p."PersonId" = o."InitiatorUserId";
                """);

            migrationBuilder.CreateTable(
                name: "known_books_locks",
                schema: "debts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_books_locks", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "operation_changes",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Draft = table.Column<string>(type: "jsonb", nullable: true),
                    Preview = table.Column<string>(type: "jsonb", nullable: true),
                    NewOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_changes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operation_change_approvals",
                schema: "debts",
                columns: table => new
                {
                    ChangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_change_approvals", x => new { x.ChangeId, x.UserId });
                    table.ForeignKey(
                        name: "FK_operation_change_approvals_operation_changes_ChangeId",
                        column: x => x.ChangeId,
                        principalSchema: "debts",
                        principalTable: "operation_changes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operations_EntryId",
                schema: "debts",
                table: "operations",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_change_approvals_UserId",
                schema: "debts",
                table: "operation_change_approvals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_changes_OperationId",
                schema: "debts",
                table: "operation_changes",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_changes_RequestedByUserId",
                schema: "debts",
                table: "operation_changes",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "known_books_locks",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "operation_change_approvals",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "operation_changes",
                schema: "debts");

            migrationBuilder.DropIndex(
                name: "IX_operations_EntryId",
                schema: "debts",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "EntryId",
                schema: "debts",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "IsDisputed",
                schema: "debts",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "PendingChangeId",
                schema: "debts",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "ReplacesOperationId",
                schema: "debts",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "debts",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "WalletId",
                schema: "debts",
                table: "operations");
        }
    }
}
