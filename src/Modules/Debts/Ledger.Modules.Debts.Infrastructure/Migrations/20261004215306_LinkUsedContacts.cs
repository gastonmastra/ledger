using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Debts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkUsedContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMerged",
                schema: "debts",
                table: "known_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "known_link_requests",
                schema: "debts",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_link_requests", x => x.RequestId);
                });

            migrationBuilder.CreateTable(
                name: "person_merges",
                schema: "debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Parties = table.Column<string>(type: "jsonb", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_merges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_person_merges_PreviousPersonId_Status",
                schema: "debts",
                table: "person_merges",
                columns: new[] { "PreviousPersonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_person_merges_RequestId",
                schema: "debts",
                table: "person_merges",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "known_link_requests",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "person_merges",
                schema: "debts");

            migrationBuilder.DropColumn(
                name: "IsMerged",
                schema: "debts",
                table: "known_contacts");
        }
    }
}
