using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CertificatesAndLatinNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "CertificateNo");

            migrationBuilder.AddColumn<string>(
                name: "FullNameEn",
                table: "Users",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatinName",
                table: "ReservedNames",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OwnerNameAr = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    OwnerNameEn = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    NationalId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Nationality = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ItemsJson = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_Number",
                table: "Certificates",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_OwnerId",
                table: "Certificates",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropColumn(
                name: "FullNameEn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LatinName",
                table: "ReservedNames");

            migrationBuilder.DropSequence(
                name: "CertificateNo");
        }
    }
}
