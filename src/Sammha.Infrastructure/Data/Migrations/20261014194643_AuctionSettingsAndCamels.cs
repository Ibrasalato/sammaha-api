using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuctionSettingsAndCamels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuctionDays",
                table: "PlatformSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManqiyaOpeningPrice",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinIncrement",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningPrice",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwnedCamels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ChipNumbers = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    Sex = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Age = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ReservedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedCamels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnedCamels_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnedCamels_ExternalId",
                table: "OwnedCamels",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnedCamels_UserId",
                table: "OwnedCamels",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnedCamels");

            migrationBuilder.DropColumn(
                name: "AuctionDays",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ManqiyaOpeningPrice",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "MinIncrement",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OpeningPrice",
                table: "PlatformSettings");
        }
    }
}
