using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkUsedContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoContactId",
                schema: "contacts",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingLinkRequestId",
                schema: "contacts",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "link_requests",
                schema: "contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_link_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_PersonId",
                schema: "contacts",
                table: "contacts",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_link_requests_ContactId",
                schema: "contacts",
                table: "link_requests",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_link_requests_OwnerId",
                schema: "contacts",
                table: "link_requests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_link_requests_TargetUserId",
                schema: "contacts",
                table: "link_requests",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "link_requests",
                schema: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contacts_PersonId",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "MergedIntoContactId",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "PendingLinkRequestId",
                schema: "contacts",
                table: "contacts");
        }
    }
}
