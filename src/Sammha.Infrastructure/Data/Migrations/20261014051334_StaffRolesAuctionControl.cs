using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffRolesAuctionControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StaffRoleId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeId",
                table: "Tickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComplaintEmails",
                table: "PlatformSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InquiryEmails",
                table: "PlatformSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReportEmails",
                table: "PlatformSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SuggestionEmails",
                table: "PlatformSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "TicketsRestrictToHandlers",
                table: "PlatformSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Auctions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Auctions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                table: "Auctions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                table: "Auctions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Permissions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketRoutes",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketRoutes", x => new { x.Kind, x.UserId });
                    table.ForeignKey(
                        name: "FK_TicketRoutes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "StaffRoles",
                columns: new[] { "Id", "CreatedAt", "Description", "IsSystem", "Name", "Permissions" },
                values: new object[] { new Guid("5a3f0000-0000-4000-8000-0000000000c1"), new DateTimeOffset(new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "مراجعة الطلبات وفتح المزادات والرد على الشكاوى", true, "لجنة الاعتماد", "overview.view,requests.view,requests.review,requests.cancel,auctions.schedule,invoices.view,tickets.view,tickets.reply,tickets.manage,events.manage,users.view" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_StaffRoleId",
                table: "Users",
                column: "StaffRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssigneeId_Status",
                table: "Tickets",
                columns: new[] { "AssigneeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoles_Name",
                table: "StaffRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketRoutes_UserId",
                table: "TicketRoutes",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_StaffRoles_StaffRoleId",
                table: "Users",
                column: "StaffRoleId",
                principalTable: "StaffRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_StaffRoles_StaffRoleId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "StaffRoles");

            migrationBuilder.DropTable(
                name: "TicketRoutes");

            migrationBuilder.DropIndex(
                name: "IX_Users_StaffRoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AssigneeId_Status",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "StaffRoleId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ComplaintEmails",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "InquiryEmails",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ReportEmails",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SuggestionEmails",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "TicketsRestrictToHandlers",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "Auctions");
        }
    }
}
