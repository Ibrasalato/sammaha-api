using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuctionGuaranteesIcoNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExceptedAt",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ExceptedBy",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ExceptionFee",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ExceptionReason",
                table: "Candidates");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ForfeitedAt",
                table: "ReservedNames",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ForfeitedDeposit",
                table: "ReservedNames",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ForfeitedFromId",
                table: "ReservedNames",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForfeitedFromName",
                table: "ReservedNames",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ForfeitedPrice",
                table: "ReservedNames",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentDeadlineHours",
                table: "PlatformSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IcoReservedId",
                table: "Auctions",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForfeitedAt",
                table: "ReservedNames");

            migrationBuilder.DropColumn(
                name: "ForfeitedDeposit",
                table: "ReservedNames");

            migrationBuilder.DropColumn(
                name: "ForfeitedFromId",
                table: "ReservedNames");

            migrationBuilder.DropColumn(
                name: "ForfeitedFromName",
                table: "ReservedNames");

            migrationBuilder.DropColumn(
                name: "ForfeitedPrice",
                table: "ReservedNames");

            migrationBuilder.DropColumn(
                name: "PaymentDeadlineHours",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IcoReservedId",
                table: "Auctions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExceptedAt",
                table: "Candidates",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptedBy",
                table: "Candidates",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExceptionFee",
                table: "Candidates",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionReason",
                table: "Candidates",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }
    }
}
