namespace Sammha.Application.Common;

// JSON contract of the React front end (src/lib/types.ts). Null members are omitted on the wire.

public record UserDto(Guid Id, string FullName, string Phone, string Email, string? NationalId, string Role, DateTimeOffset CreatedAt,
    string? AvatarUrl, bool? ShowAvatar, bool? Active);

public record SimilarHitDto(string Name, string Type, string Source, double Score);

public record CandidateDto(Guid Id, string Name, string Normalized, string Status, List<SimilarHitDto> Similar, string? RejectReason,
    Guid? AuctionId, DateTimeOffset? WithdrawnAt, bool? Withdrawable);

public record RequestDto(Guid Id, string Number, Guid UserId, string OwnerName, string Type, List<CandidateDto> Candidates, string Status,
    decimal DepositAmount, bool DepositPaid, string DepositState, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ReviewedAt,
    string? ReviewerName, string? ReviewNote, bool? Cancellable, DateTimeOffset? CancelledAt, string? CancelledBy, string? CancelReason,
    string? Kind, string? BaseName, Guid? BaseReservedId);

public record AuctionDto(Guid Id, Guid? RequestId, Guid? CandidateId, string Name, string Type, Guid RequesterId, decimal OpeningPrice, decimal MinIncrement,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal CurrentPrice, int BidCount, Guid? LeaderId, string? LeaderLabel, bool Settled,
    Guid? WinnerId, string? WinnerName, decimal? FinalPrice, bool? WonByRequester, int? Extensions, bool? Investment, Guid? ListingId, decimal? BidDeposit);

public record BidDto(Guid Id, Guid AuctionId, Guid UserId, string BidderLabel, decimal Amount, DateTimeOffset CreatedAt, bool? Auto);

public record MyBidDto(Guid Id, Guid AuctionId, Guid UserId, string BidderLabel, decimal Amount, DateTimeOffset CreatedAt, bool? Auto, AuctionDto Auction);

public record AuctionMineDto(bool Watching, decimal? AutoMax, bool? DepositPaid);

public record AuctionViewDto(AuctionDto Auction, List<BidDto> Bids, AuctionMineDto? Mine);

public record ReservedNameDto(Guid Id, string Name, string Normalized, string Type, Guid? OwnerId, string OwnerName, decimal Price, DateTimeOffset AcquiredAt,
    string Source, string? BaseName, string? OwnerAvatar, Guid? ListingId);

public record WalletTxDto(Guid Id, Guid UserId, decimal Amount, string Kind, string Note, DateTimeOffset CreatedAt);
public record WalletDto(decimal Balance, List<WalletTxDto> Txs);

public record InvoiceDto(Guid Id, string Number, Guid UserId, string BuyerName, string BuyerPhone, Guid? AuctionId, string Name, string Type,
    decimal Total, decimal Vat, decimal Subtotal, decimal DepositApplied, decimal AmountDue, string Status, DateTimeOffset IssuedAt, DateTimeOffset? PaidAt,
    decimal? OwnerShare, Guid? OwnerShareTo, bool? OwnerSharePaid, Guid? ListingId);

public record ListingDto(Guid Id, string Number, Guid ReservedId, string Name, string Type, Guid? SellerId, string Mode, decimal Price, decimal CommissionRate,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid? AuctionId, decimal? SoldPrice, DateTimeOffset? SoldAt,
    decimal? Payout, bool? PayoutPaid, decimal? CurrentPrice, int? BidCount);

public record InvestmentItemDto(RequestDto Request, CandidateDto Candidate, AuctionDto? Auction, decimal? Share, bool? SharePaid, bool? KeptByOwner);

public record NameCheckDto(string Name, string Normalized, string Verdict, string Message, List<SimilarHitDto> Hits);

public record PublicStatsDto(int Live, int Upcoming, int Reserved, int Owners);

public record AdminOverviewDto(int UnderReview, int AwaitingSchedule, int Live, int Scheduled, int UnpaidInvoices, decimal Revenue, int OpenTickets);

public record CamelEventDto(string Id, string Title, string TitleEn, string Kind, string StartsOn, string EndsOn, bool Approx, string City, string Country,
    string Description, string? Url, string? CityEn, string? CountryEn, string? DescriptionEn);

public record NotificationDto(Guid Id, Guid UserId, string Kind, string Title, string Body, string? Link, DateTimeOffset CreatedAt, bool Read);

public record TicketMessageDto(Guid Id, string By, string Author, string Body, bool? Internal, DateTimeOffset CreatedAt);

public record TicketDto(Guid Id, string Number, string Kind, string Topic, string Subject, string Name, string Phone, string? Email, string? RefNumber,
    string Message, Guid? UserId, string Status, string Priority, string? Assignee, List<TicketMessageDto> Messages, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>a candidate waiting for an auction date, with its request (committee «فتح المزادات»)</summary>
public record AwaitingDto(Guid Id, string Name, string Normalized, string Status, List<SimilarHitDto> Similar, string? RejectReason,
    Guid? AuctionId, DateTimeOffset? WithdrawnAt, bool? Withdrawable, RequestDto Request);

public record OtpChallengeDto(string Challenge, string PhoneMasked, DateTimeOffset ExpiresAt, int? Length);

public record AuthResult(UserDto User, string Token);

// ───── inputs ─────
public record SignUpInput(string FullName, string Phone, string Email, string? NationalId, string Password);
public record TicketInput(string Kind, string Topic, string Subject, string Name, string Phone, string? Email, string? RefNumber, string Message);
public record ReviewDecision(Guid CandidateId, string Status, string? Reason);
public record StaffInput(string FullName, string Phone, string Email, string Password, string Role);
public record AuctionFilter(string? Phase, string? Type, string? Q);
