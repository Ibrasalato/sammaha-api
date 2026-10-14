using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ViolationsFestivalsCamelLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "ViolationNo");

            migrationBuilder.CreateTable(
                name: "Festivals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Festivals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ViolationTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    AllowsNameResolution = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViolationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Violations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FestivalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FestivalName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CamelChip = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AttachmentName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AttachmentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AttachmentBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IssuedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Violations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Violations_Festivals_FestivalId",
                        column: x => x.FestivalId,
                        principalTable: "Festivals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Violations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Violations_ViolationTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "ViolationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnedCamels_ReservedId",
                table: "OwnedCamels",
                column: "ReservedId");

            migrationBuilder.CreateIndex(
                name: "IX_Festivals_Name",
                table: "Festivals",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_FestivalId",
                table: "Violations",
                column: "FestivalId");

            migrationBuilder.CreateIndex(
                name: "IX_Violations_Number",
                table: "Violations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_Status_IssuedAt",
                table: "Violations",
                columns: new[] { "Status", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Violations_TypeId",
                table: "Violations",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Violations_UserId_IssuedAt",
                table: "Violations",
                columns: new[] { "UserId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ViolationTypes_Name",
                table: "ViolationTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Violations");

            migrationBuilder.DropTable(
                name: "Festivals");

            migrationBuilder.DropTable(
                name: "ViolationTypes");

            migrationBuilder.DropIndex(
                name: "IX_OwnedCamels_ReservedId",
                table: "OwnedCamels");

            migrationBuilder.DropSequence(
                name: "ViolationNo");
        }
    }
}
