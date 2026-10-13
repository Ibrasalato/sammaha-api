using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sammha.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "InvoiceNo");

            migrationBuilder.CreateSequence<int>(
                name: "ListingNo",
                startValue: 1001L);

            migrationBuilder.CreateSequence<int>(
                name: "RequestNo",
                startValue: 1001L);

            migrationBuilder.CreateSequence<int>(
                name: "TicketNo");

            migrationBuilder.CreateTable(
                name: "Auctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MinIncrement = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    BidCount = table.Column<int>(type: "int", nullable: false),
                    LeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Settled = table.Column<bool>(type: "bit", nullable: false),
                    WinnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    WonByRequester = table.Column<bool>(type: "bit", nullable: true),
                    Extensions = table.Column<int>(type: "int", nullable: false),
                    IsInvestment = table.Column<bool>(type: "bit", nullable: false),
                    BidDeposit = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auctions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoBids",
                columns: table => new
                {
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Max = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoBids", x => new { x.AuctionId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "BidDeposits",
                columns: table => new
                {
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    State = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidDeposits", x => new { x.AuctionId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "Bids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Auto = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bids", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StartsOn = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EndsOn = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Approx = table.Column<bool>(type: "bit", nullable: false),
                    City = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CityEn = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CountryEn = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DescriptionEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReservedId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SoldPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SoldAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Payout = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    PayoutPaid = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Read = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Used = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservedNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Normalized = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    BaseName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CertCode = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedNames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RefNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Assignee = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    NationalId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: true),
                    ShowAvatar = table.Column<bool>(type: "bit", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTxs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTxs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Watches",
                columns: table => new
                {
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotifiedStart = table.Column<bool>(type: "bit", nullable: false),
                    NotifiedEnd = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Watches", x => new { x.AuctionId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "TicketMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    By = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    Internal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketMessages_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DepositApplied = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AmountDue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OwnerShare = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    OwnerShareTo = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerSharePaid = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DepositPaid = table.Column<bool>(type: "bit", nullable: false),
                    DepositState = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    BaseReservedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaseName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Requests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Normalized = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SimilarJson = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidates_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_Settled_EndsAt",
                table: "Auctions",
                columns: new[] { "Settled", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_StartsAt",
                table: "Auctions",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_AuctionId_Amount",
                table: "Bids",
                columns: new[] { "AuctionId", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_Normalized",
                table: "Candidates",
                column: "Normalized");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_RequestId",
                table: "Candidates",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_AuctionId",
                table: "Invoices",
                column: "AuctionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Number",
                table: "Invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId",
                table: "Invoices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Mode_Status",
                table: "Listings",
                columns: new[] { "Mode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Number",
                table: "Listings",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Listings_ReservedId_Status",
                table: "Listings",
                columns: new[] { "ReservedId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderId",
                table: "Payments",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Number",
                table: "Requests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status",
                table: "Requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_UserId_CreatedAt",
                table: "Requests",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservedNames_CertCode",
                table: "ReservedNames",
                column: "CertCode");

            migrationBuilder.CreateIndex(
                name: "IX_ReservedNames_Normalized",
                table: "ReservedNames",
                column: "Normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservedNames_OwnerId",
                table: "ReservedNames",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId",
                table: "TicketMessages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Number",
                table: "Tickets",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Phone",
                table: "Tickets",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTxs_UserId_CreatedAt",
                table: "WalletTxs",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Auctions");

            migrationBuilder.DropTable(
                name: "AutoBids");

            migrationBuilder.DropTable(
                name: "BidDeposits");

            migrationBuilder.DropTable(
                name: "Bids");

            migrationBuilder.DropTable(
                name: "Candidates");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Listings");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OtpChallenges");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "ReservedNames");

            migrationBuilder.DropTable(
                name: "TicketMessages");

            migrationBuilder.DropTable(
                name: "WalletTxs");

            migrationBuilder.DropTable(
                name: "Watches");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropSequence(
                name: "InvoiceNo");

            migrationBuilder.DropSequence(
                name: "ListingNo");

            migrationBuilder.DropSequence(
                name: "RequestNo");

            migrationBuilder.DropSequence(
                name: "TicketNo");
        }
    }
}
