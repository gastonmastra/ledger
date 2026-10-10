using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkUsedContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoPersonId",
                schema: "transactions",
                table: "counterparties",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MergedIntoPersonId",
                schema: "transactions",
                table: "counterparties");
        }
    }
}
