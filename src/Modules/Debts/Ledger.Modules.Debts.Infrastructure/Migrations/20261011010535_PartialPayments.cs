using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Debts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Paid",
                schema: "debts",
                table: "debts",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "debt_payments",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebtId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_payments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_debt_payments_DebtId",
                schema: "debts",
                table: "debt_payments",
                column: "DebtId");

            migrationBuilder.CreateIndex(
                name: "IX_debt_payments_SettlementOperationId",
                schema: "debts",
                table: "debt_payments",
                column: "SettlementOperationId");

            // Backfill: settlements recorded before partial payments paid whole debts. Outside groups each of those
            // debts gets one payment row for its whole amount (also while its settlement is still being recorded), and
            // it no longer points at the settlement: payments are tracked by rows from now on. Group debts keep it.
            migrationBuilder.Sql("""
                INSERT INTO debts.debt_payments ("Id", "SettlementOperationId", "DebtId", "Amount", "CreatedAt")
                SELECT gen_random_uuid(), d."SettlementOperationId", d."Id", d."Amount", now()
                FROM debts.debts d
                WHERE d."GroupId" IS NULL AND d."SettlementOperationId" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE debts.debts SET "Paid" = "Amount" WHERE "Status" = 'Settled';
                """);

            migrationBuilder.Sql("""
                UPDATE debts.debts SET "SettlementOperationId" = NULL
                WHERE "GroupId" IS NULL AND "SettlementOperationId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best effort: a fully paid debt points again at the last current payment that paid it. Partial payments
            // cannot be represented before this migration and are lost.
            migrationBuilder.Sql("""
                UPDATE debts.debts d SET "SettlementOperationId" = p."SettlementOperationId"
                FROM (
                    SELECT DISTINCT ON (r."DebtId") r."DebtId", r."SettlementOperationId"
                    FROM debts.debt_payments r
                    JOIN debts.operations o ON o."Id" = r."SettlementOperationId"
                    WHERE o."Status" IN ('Pending', 'Completed')
                    ORDER BY r."DebtId", o."OccurredAt" DESC
                ) p
                WHERE d."Id" = p."DebtId" AND d."GroupId" IS NULL AND d."Status" = 'Settled';
                """);

            migrationBuilder.DropTable(
                name: "debt_payments",
                schema: "debts");

            migrationBuilder.DropColumn(
                name: "Paid",
                schema: "debts",
                table: "debts");
        }
    }
}
