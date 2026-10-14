using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CrmFinanceReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "AuctionNo",
                startValue: 1001L);

            migrationBuilder.CreateSequence<int>(
                name: "WithdrawalNo",
                startValue: 1001L);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "Auctions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DocName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DocType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Withdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ByStaff = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TransferRef = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Withdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Withdrawals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_Number",
                table: "Auctions",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_Status",
                table: "BankAccounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_Status_CreatedAt",
                table: "Withdrawals",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_UserId",
                table: "Withdrawals",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_Auctions_Number",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Auctions");

            migrationBuilder.DropSequence(
                name: "AuctionNo");

            migrationBuilder.DropSequence(
                name: "WithdrawalNo");
        }
    }
}
