using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLogins",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOutbid",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyReminders",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyRequests",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyResults",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RequestsOpen = table.Column<bool>(type: "bit", nullable: false),
                    RequestsClosedNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CompoundOpen = table.Column<bool>(type: "bit", nullable: false),
                    CompoundClosedNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResaleAuctionOpen = table.Column<bool>(type: "bit", nullable: false),
                    InstantSaleOpen = table.Column<bool>(type: "bit", nullable: false),
                    SaleClosedNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InvestmentOpen = table.Column<bool>(type: "bit", nullable: false),
                    InvestmentClosedNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SignupOpen = table.Column<bool>(type: "bit", nullable: false),
                    SupportEmail = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SupportPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WhatsApp = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AnnouncementOn = table.Column<bool>(type: "bit", nullable: false),
                    Announcement = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AnnouncementTone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Maintenance = table.Column<bool>(type: "bit", nullable: false),
                    MaintenanceNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PasswordMinLength = table.Column<int>(type: "int", nullable: false),
                    MaxLoginAttempts = table.Column<int>(type: "int", nullable: false),
                    LockoutMinutes = table.Column<int>(type: "int", nullable: false),
                    StaffIdleMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                    table.CheckConstraint("CK_PlatformSettings_Single", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "SettingsChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettingsChanges_At",
                table: "SettingsChanges",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "SettingsChanges");

            migrationBuilder.DropColumn(
                name: "FailedLogins",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyOutbid",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyReminders",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyRequests",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyResults",
                table: "Users");
        }
    }
}
