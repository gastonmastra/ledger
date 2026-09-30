using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ledger.Modules.Debts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "debts");

            migrationBuilder.CreateTable(
                name: "debts",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreditorPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebtorPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SettlementOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "debts",
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
                schema: "debts",
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
                name: "known_contacts",
                schema: "debts",
                columns: table => new
                {
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LinkedUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_contacts", x => x.ContactId);
                });

            migrationBuilder.CreateTable(
                name: "known_group_members",
                schema: "debts",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_group_members", x => new { x.GroupId, x.PersonId });
                });

            migrationBuilder.CreateTable(
                name: "known_users",
                schema: "debts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "known_wallets",
                schema: "debts",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_wallets", x => x.WalletId);
                });

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FromPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operation_payers",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_payers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operation_payers_operations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "debts",
                        principalTable: "operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operation_shares",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operation_shares_operations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "debts",
                        principalTable: "operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_debts_CreditorPersonId",
                schema: "debts",
                table: "debts",
                column: "CreditorPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_debts_DebtorPersonId",
                schema: "debts",
                table: "debts",
                column: "DebtorPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_debts_GroupId",
                schema: "debts",
                table: "debts",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_debts_OperationId",
                schema: "debts",
                table: "debts",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_debts_SettlementOperationId",
                schema: "debts",
                table: "debts",
                column: "SettlementOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_OccurredAt",
                schema: "debts",
                table: "integration_outbox",
                column: "OccurredAt",
                filter: "\"ProcessedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_known_contacts_OwnerId_PersonId",
                schema: "debts",
                table: "known_contacts",
                columns: new[] { "OwnerId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_known_group_members_UserId",
                schema: "debts",
                table: "known_group_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_payers_OperationId",
                schema: "debts",
                table: "operation_payers",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_shares_OperationId",
                schema: "debts",
                table: "operation_shares",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_operations_GroupId_Status",
                schema: "debts",
                table: "operations",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_operations_InitiatorUserId",
                schema: "debts",
                table: "operations",
                column: "InitiatorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debts",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "integration_outbox",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "known_contacts",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "known_group_members",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "known_users",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "known_wallets",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "operation_payers",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "operation_shares",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "debts");
        }
    }
}
