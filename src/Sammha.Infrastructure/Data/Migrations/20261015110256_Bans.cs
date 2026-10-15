using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Bans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    LiftedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LiftedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    LiftReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bans_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bans_CreatedAt",
                table: "Bans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Bans_NationalId",
                table: "Bans",
                column: "NationalId");

            migrationBuilder.CreateIndex(
                name: "IX_Bans_UserId",
                table: "Bans",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bans");
        }
    }
}
