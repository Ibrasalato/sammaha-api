using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LegacyImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InvestorId",
                table: "Auctions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvestorSharePercent",
                table: "Auctions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegacyLinks",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LegacyId = table.Column<int>(type: "int", nullable: false),
                    NewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyLinks", x => new { x.Kind, x.LegacyId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyLinks_NewId",
                table: "LegacyLinks",
                column: "NewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyLinks");

            migrationBuilder.DropColumn(
                name: "InvestorId",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "InvestorSharePercent",
                table: "Auctions");
        }
    }
}
