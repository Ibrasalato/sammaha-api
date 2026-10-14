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
    /// <summary>changes on password change or suspension; access tokens carrying an old stamp stop working at once</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    // notification preferences: in-app notices always arrive; these switch the SMS / WhatsApp copy per category
    public bool NotifyOutbid { get; set; } = true;
    public bool NotifyReminders { get; set; } = true;
    public bool NotifyResults { get; set; } = true;
    public bool NotifyRequests { get; set; } = true;

    /// <summary>wrong passwords in a row; reaching the limit locks sign-in until LockedUntil</summary>
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>committee members: the staff role that sets their permissions (null = the default committee role)</summary>
    public Guid? StaffRoleId { get; set; }

    // ───── owner profile (sign-up) ─────
    public string? FirstName { get; set; }
    public string? FatherName { get; set; }
    public string? GrandfatherName { get; set; }
    public string? FamilyName { get; set; }
    public DateOnly? BirthDate { get; set; }
    /// <summary>male | female</summary>
    public string? Gender { get; set; }
    /// <summary>ISO 3166 alpha-2</summary>
    public string? Nationality { get; set; }
    public string? ResidenceCountry { get; set; }
    public string? ResidenceCity { get; set; }
    /// <summary>set once the owner confirms a code sent to the email</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    // ───── completed later in the dashboard ─────
    public string? AltPhone { get; set; }
    public string? AddressCountry { get; set; }
    public string? AddressCity { get; set; }
    public string? District { get; set; }
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
}

/// <summary>
/// Platform settings the super admin changes from the dashboard (one row, Id = 1).
/// Fees and rules stay in configuration (PlatformOptions); these are the operational switches.
/// </summary>
public class PlatformSettings
{
    public int Id { get; set; } = 1;

    // ───── owner requests intake ─────
    /// <summary>new logo (name) requests</summary>
    public bool RequestsOpen { get; set; } = true;
    public string? RequestsClosedNote { get; set; }
    /// <summary>compound names for a name the owner holds</summary>
    public bool CompoundOpen { get; set; } = true;
    public string? CompoundClosedNote { get; set; }
    /// <summary>«بيع اسم»: resale auctions and fixed-price sales</summary>
    public bool ResaleAuctionOpen { get; set; } = true;
    public bool InstantSaleOpen { get; set; } = true;
    public string? SaleClosedNote { get; set; }
    /// <summary>«استثمار الاسم»</summary>
    public bool InvestmentOpen { get; set; } = true;
    public string? InvestmentClosedNote { get; set; }

    // ───── general ─────
    public bool SignupOpen { get; set; } = true;
    public string SupportEmail { get; set; } = "";
    public string SupportPhone { get; set; } = "";
    public string WhatsApp { get; set; } = "";
    public bool AnnouncementOn { get; set; }
    public string? Announcement { get; set; }
    /// <summary>info | warn | ok</summary>
    public string AnnouncementTone { get; set; } = "info";
    /// <summary>owners can browse but not submit, bid or pay; staff work normally</summary>
    public bool Maintenance { get; set; }
    public string? MaintenanceNote { get; set; }

    // ───── security ─────
    public int PasswordMinLength { get; set; } = 8;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    /// <summary>staff dashboards sign out after this many idle minutes (0 = never)</summary>
    public int StaffIdleMinutes { get; set; } = 30;

    // ───── complaints, reports and enquiries ─────
    /// <summary>when on, only the handlers of a ticket's kind (and admins) can reply to it or change it</summary>
    public bool TicketsRestrictToHandlers { get; set; }
    /// <summary>team inboxes per kind (comma-separated): new tickets and follow-ups of that kind are emailed there</summary>
    public string ComplaintEmails { get; set; } = "";
    public string ReportEmails { get; set; } = "";
    public string InquiryEmails { get; set; } = "";
    public string SuggestionEmails { get; set; } = "";

    public string EmailsFor(string kind) => kind switch
    {
        TicketKinds.Complaint => ComplaintEmails,
        TicketKinds.Report => ReportEmails,
        TicketKinds.Inquiry => InquiryEmails,
        TicketKinds.Suggestion => SuggestionEmails,
        _ => "",
    };

    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Who handles which kind of ticket (complaint, report, inquiry, suggestion). New tickets are assigned among them.</summary>
public class TicketRoute
{
    public string Kind { get; set; } = "";
    public Guid UserId { get; set; }
}

/// <summary>Audit trail of settings changes and security actions.</summary>
public class SettingsChange
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>Long-lived token that buys new access tokens. Only its SHA-256 hash is stored; each use rotates it.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    /// <summary>all tokens rotated from one sign-in share a family; reuse of a rotated token revokes the whole family</summary>
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt == null && now < ExpiresAt;
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
    /// <summary>serial auction number, AU-1001 onwards</summary>
    public string Number { get; set; } = "";
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
    /// <summary>paused by the administration: no bids, no settlement; resuming adds the paused time back</summary>
    public DateTimeOffset? PausedAt { get; set; }
    public string? PauseReason { get; set; }
    /// <summary>cancelled by the administration: closed with no sale</summary>
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public string PhaseAt(DateTimeOffset now) => CancelledAt != null ? "ended" : now < StartsAt ? "scheduled" : now < EndsAt ? "live" : "ended";
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

/// <summary>money paid out of an owner's wallet to their bank account; the amount leaves the wallet when requested and returns if rejected</summary>
public class Withdrawal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Iban { get; set; } = "";
    public string BankName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Status { get; set; } = WithdrawalStatuses.Pending;
    /// <summary>true when the finance team started it (no owner request)</summary>
    public bool ByStaff { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }
    public string? TransferRef { get; set; }
    public string? Note { get; set; }
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
    /// <summary>the staff member the ticket is assigned to (set by routing or by hand)</summary>
    public Guid? AssigneeId { get; set; }
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
