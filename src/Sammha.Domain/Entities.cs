namespace Sammha.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string? NationalId { get; set; }
    public string Role { get; set; } = Roles.Owner;
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? AvatarUrl { get; set; }
    public bool ShowAvatar { get; set; }
    public bool Active { get; set; } = true;
}

public class LogoRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Type { get; set; } = NameTypes.Qaadan;
    public string Kind { get; set; } = RequestKinds.Standard;
    public string Status { get; set; } = RequestStatuses.PendingPayment;
    public decimal DepositAmount { get; set; }
    public bool DepositPaid { get; set; }
    public string DepositState { get; set; } = DepositStates.None;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    /// <summary>compound requests: the registered name it builds on</summary>
    public Guid? BaseReservedId { get; set; }
    public string? BaseName { get; set; }
    public List<Candidate> Candidates { get; set; } = [];
}

public class Candidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public LogoRequest Request { get; set; } = null!;
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Normalized { get; set; } = "";
    public string Status { get; set; } = CandidateStatuses.Pending;
    /// <summary>JSON array of SimilarHit found at check time, for the committee</summary>
    public string SimilarJson { get; set; } = "[]";
    public string? RejectReason { get; set; }
    public Guid? AuctionId { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

public class Auction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RequestId { get; set; }
    public Guid? CandidateId { get; set; }
    /// <summary>set for «بيع اسم» resale auctions</summary>
    public Guid? ListingId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = NameTypes.Qaadan;
    public Guid RequesterId { get; set; }
    public decimal OpeningPrice { get; set; }
    public decimal MinIncrement { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public decimal CurrentPrice { get; set; }
    public int BidCount { get; set; }
    public Guid? LeaderId { get; set; }
    public bool Settled { get; set; }
    public Guid? WinnerId { get; set; }
    public decimal? FinalPrice { get; set; }
    public bool? WonByRequester { get; set; }
    public int Extensions { get; set; }
    public bool IsInvestment { get; set; }
    public decimal? BidDeposit { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public string PhaseAt(DateTimeOffset now) => now < StartsAt ? "scheduled" : now < EndsAt ? "live" : "ended";
}

public class Bid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuctionId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Auto { get; set; }
}

public class AutoBid
{
    public Guid AuctionId { get; set; }
    public Guid UserId { get; set; }
    public decimal Max { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Watch
{
    public Guid AuctionId { get; set; }
    public Guid UserId { get; set; }
    public bool NotifiedStart { get; set; }
    public bool NotifiedEnd { get; set; }
}

public class BidDeposit
{
    public Guid AuctionId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = BidDepositStates.Held;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReservedName
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Normalized { get; set; } = "";
    public string Type { get; set; } = NameTypes.Qaadan;
    public Guid? OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public string Source { get; set; } = ReservedSources.Legacy;
    public string? BaseName { get; set; }
    public Guid? AuctionId { get; set; }
    /// <summary>public certificate code (SMC-XXXXXXXX), derived from the id</summary>
    public string CertCode { get; set; } = "";

    public static string CertFor(Guid id) => "SMC-" + id.ToString("N")[^8..].ToUpperInvariant();
}

public class WalletTx
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Kind { get; set; } = TxKinds.TopUp;
    public string Note { get; set; } = "";
    public Guid? Ref { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? AuctionId { get; set; }
    public Guid? ListingId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = NameTypes.Qaadan;
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DepositApplied { get; set; }
    public decimal AmountDue { get; set; }
    public string Status { get; set; } = InvoiceStatuses.Unpaid;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>investment: the proposer's 30%; sale: the seller's price after commission</summary>
    public decimal? OwnerShare { get; set; }
    public Guid? OwnerShareTo { get; set; }
    public bool OwnerSharePaid { get; set; }
}

public class NameListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid ReservedId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = NameTypes.Qaadan;
    public Guid SellerId { get; set; }
    public string Mode { get; set; } = ListingModes.Auction;
    public decimal Price { get; set; }
    public decimal CommissionRate { get; set; }
    public string Status { get; set; } = ListingStatuses.Scheduled;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public Guid? AuctionId { get; set; }
    public decimal? SoldPrice { get; set; }
    public DateTimeOffset? SoldAt { get; set; }
    public decimal? Payout { get; set; }
    public bool PayoutPaid { get; set; }
}

public class CamelEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string Kind { get; set; } = "mazayin";
    public string StartsOn { get; set; } = "";
    public string EndsOn { get; set; } = "";
    public bool Approx { get; set; }
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Url { get; set; }
    public string? CityEn { get; set; }
    public string? CountryEn { get; set; }
    public string? DescriptionEn { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Read { get; set; }
}

public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public string Kind { get; set; } = TicketKinds.Inquiry;
    public string Topic { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string? RefNumber { get; set; }
    public string Message { get; set; } = "";
    public Guid? UserId { get; set; }
    public string Status { get; set; } = TicketStatuses.New;
    public string Priority { get; set; } = TicketPriorities.Normal;
    public string? Assignee { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TicketMessage> Messages { get; set; } = [];
}

public class TicketMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string By { get; set; } = "customer";
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Internal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OtpChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = OtpPurposes.Login;
    /// <summary>where the code was sent: phone number or email</summary>
    public string Target { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Used { get; set; }
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = PaymentPurposes.TopUp;
    public Guid? Ref { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "initiated";
    public string? ProviderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}
