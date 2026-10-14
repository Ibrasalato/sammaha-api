using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServicePrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CompoundFee",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InstantCommission",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvestFee",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ResaleCommission",
                table: "PlatformSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceTextsJson",
                table: "PlatformSettings",
                type: "nvarchar(max)",
                maxLength: 400,
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompoundFee",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "InstantCommission",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "InvestFee",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ResaleCommission",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ServiceTextsJson",
                table: "PlatformSettings");
        }
    }
}
