using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transactions");

            migrationBuilder.CreateTable(
                name: "counterparties",
                schema: "transactions",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparties", x => new { x.OwnerId, x.PersonId });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "transactions",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    TransactionCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pending_wallet_assignments",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_wallet_assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "postings",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_postings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_postings_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "transactions",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_counterparties_OwnerId_SourceId",
                schema: "transactions",
                table: "counterparties",
                columns: new[] { "OwnerId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_OccurredAt",
                schema: "transactions",
                table: "integration_outbox",
                column: "OccurredAt",
                filter: "\"ProcessedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_OwnerId_Purpose",
                schema: "transactions",
                table: "ledger_accounts",
                columns: new[] { "OwnerId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_ReferenceId",
                schema: "transactions",
                table: "ledger_accounts",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_pending_wallet_assignments_OwnerId_Status",
                schema: "transactions",
                table: "pending_wallet_assignments",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_postings_AccountId",
                schema: "transactions",
                table: "postings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_postings_CounterpartyId",
                schema: "transactions",
                table: "postings",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_postings_TransactionId",
                schema: "transactions",
                table: "postings",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_CorrelationId",
                schema: "transactions",
                table: "transactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OwnerId_OccurredAt",
                schema: "transactions",
                table: "transactions",
                columns: new[] { "OwnerId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counterparties",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "integration_outbox",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "pending_wallet_assignments",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "postings",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "transactions");
        }
    }
}
