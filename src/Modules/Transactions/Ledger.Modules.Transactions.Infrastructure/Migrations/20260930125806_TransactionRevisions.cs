using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TransactionRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeAction",
                schema: "transactions",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                schema: "transactions",
                table: "transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedByUserId",
                schema: "transactions",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntryId",
                schema: "transactions",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfId",
                schema: "transactions",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedById",
                schema: "transactions",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "transactions",
                table: "transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing transactions become revision 1 of their own entry.
            migrationBuilder.Sql("""UPDATE transactions.transactions SET "EntryId" = "Id", "Revision" = 1;""");

            // Opening balances replaced with the previous mechanism: the successive opening balances of a
            // wallet become revisions of one entry, and each reversal is linked to what it cancelled.
            migrationBuilder.Sql("""
                WITH chain AS (
                    SELECT "Id",
                           FIRST_VALUE("Id") OVER w AS entry,
                           ROW_NUMBER() OVER w AS revision
                    FROM transactions.transactions
                    WHERE "Kind" = 'OpeningBalance'
                    WINDOW w AS (PARTITION BY "OwnerId", "CorrelationId" ORDER BY "CreatedAt", "Id"))
                UPDATE transactions.transactions AS t
                SET "EntryId" = c.entry, "Revision" = c.revision
                FROM chain AS c
                WHERE t."Id" = c."Id";

                UPDATE transactions.transactions AS r
                SET "ReversalOfId" = t."Id", "EntryId" = t."EntryId", "Revision" = t."Revision"
                FROM transactions.transactions AS t
                WHERE r."Kind" = 'OpeningBalanceReversal' AND t."Id" = r."CorrelationId";

                UPDATE transactions.transactions AS t
                SET "ReversedById" = r."Id"
                FROM transactions.transactions AS r
                WHERE r."Kind" = 'OpeningBalanceReversal' AND r."CorrelationId" = t."Id";
                """);

            migrationBuilder.CreateTable(
                name: "books_locks",
                schema: "transactions",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_books_locks", x => x.OwnerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_EntryId",
                schema: "transactions",
                table: "transactions",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ReversedById",
                schema: "transactions",
                table: "transactions",
                column: "ReversedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "books_locks",
                schema: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_EntryId",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_ReversedById",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ChangeAction",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ChangedByUserId",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "EntryId",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ReversalOfId",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ReversedById",
                schema: "transactions",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "transactions",
                table: "transactions");
        }
    }
}
