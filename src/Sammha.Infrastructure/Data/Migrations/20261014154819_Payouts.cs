using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Payouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutError",
                table: "Withdrawals",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutProvider",
                table: "Withdrawals",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutRef",
                table: "Withdrawals",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayoutSentAt",
                table: "Withdrawals",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutError",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "PayoutProvider",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "PayoutRef",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "PayoutSentAt",
                table: "Withdrawals");
        }
    }
}
