using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Exemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "ExemptionNo");

            migrationBuilder.CreateTable(
                name: "Exemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CamelChip = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CamelType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FestivalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FestivalName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Race = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    OwnerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    StaffNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PricedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PricedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PricedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerNameAr = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    OwnerNameEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Signature = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exemptions_Festivals_FestivalId",
                        column: x => x.FestivalId,
                        principalTable: "Festivals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exemptions_CamelChip_FestivalId",
                table: "Exemptions",
                columns: new[] { "CamelChip", "FestivalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Exemptions_FestivalId",
                table: "Exemptions",
                column: "FestivalId");

            migrationBuilder.CreateIndex(
                name: "IX_Exemptions_Number",
                table: "Exemptions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exemptions_Status_CreatedAt",
                table: "Exemptions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Exemptions_UserId_CreatedAt",
                table: "Exemptions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Exemptions");

            migrationBuilder.DropSequence(
                name: "ExemptionNo");
        }
    }
}
