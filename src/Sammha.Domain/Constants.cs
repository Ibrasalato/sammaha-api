namespace Sammha.Domain;

/// <summary>String values shared with the React front end (stored as-is in the database).</summary>
public static class NameTypes
{
    public const string Qaadan = "qaadan", Bakkar = "bakkar", Manqiya = "manqiya";
    public static readonly string[] All = [Qaadan, Bakkar, Manqiya];
    public static bool IsValid(string? t) => t is not null && All.Contains(t);
    public static readonly Dictionary<string, string> Arabic = new() { [Qaadan] = "القعدان", [Bakkar] = "البكار", [Manqiya] = "المنقية" };
}

public static class Roles
{
    public const string Owner = "owner", Committee = "committee", Admin = "admin";
    public static readonly string[] All = [Owner, Committee, Admin];
    public static bool IsStaff(string role) => role is Committee or Admin;
}

public static class RequestStatuses
{
    public const string PendingPayment = "pending_payment", UnderReview = "under_review", Accepted = "accepted", Rejected = "rejected", Cancelled = "cancelled";
}

public static class RequestKinds
{
    public const string Standard = "standard", Compound = "compound", Investment = "investment";
}

public static class DepositStates
{
    public const string None = "none", Held = "held", Applied = "applied", Refunded = "refunded", Fee = "fee", Forfeited = "forfeited";
}

public static class CandidateStatuses
{
    public const string Pending = "pending", Accepted = "accepted", Rejected = "rejected", Withdrawn = "withdrawn";
}

public static class ReservedSources
{
    public const string Auction = "auction", Legacy = "legacy", Compound = "compound", Investment = "investment", Resale = "resale", Exception = "exception", Transfer = "transfer";
    /// <summary>the owner of names that passed to the organization</summary>
    public const string IcoOwnerName = "المنظمة الدولية للإبل";
}

public static class TxKinds
{
    public const string TopUp = "topup", Deposit = "deposit", Refund = "refund", InvoicePayment = "invoice_payment", ServiceFee = "service_fee",
        InvestmentPayout = "investment_payout", BidDeposit = "bid_deposit", SalePayout = "sale_payout", Purchase = "purchase",
        Withdrawal = "withdrawal", Adjustment = "adjustment", Fine = "fine";
}

public static class WithdrawalStatuses
{
    public const string Pending = "pending", Processing = "processing", Completed = "completed", Rejected = "rejected";
}

public static class InvoiceStatuses
{
    public const string Paid = "paid", Unpaid = "unpaid", Cancelled = "cancelled";
}

public static class ListingModes
{
    public const string Auction = "auction", Instant = "instant";
}

public static class ListingStatuses
{
    public const string Scheduled = "scheduled", Live = "live", Sold = "sold", Unsold = "unsold", Expired = "expired", Cancelled = "cancelled";
}

public static class BidDepositStates
{
    public const string Held = "held", Refunded = "refunded", Applied = "applied", Forfeited = "forfeited";
}

public static class NotificationKinds
{
    public const string Outbid = "outbid", Won = "won", Lost = "lost", Reviewed = "reviewed", AuctionOpened = "auction_opened", AuctionUpdate = "auction_update", Refund = "refund", Ticket = "ticket", Reminder = "reminder", Delegation = "delegation", Certificate = "certificate", Transfer = "transfer", Violation = "violation";
}

public static class TicketKinds
{
    public const string Complaint = "complaint", Report = "report", Inquiry = "inquiry", Suggestion = "suggestion";
    public static readonly string[] All = [Complaint, Report, Inquiry, Suggestion];
    public static readonly Dictionary<string, string> Arabic = new() { [Complaint] = "شكوى", [Report] = "بلاغ", [Inquiry] = "استفسار", [Suggestion] = "اقتراح" };
}

public static class TicketStatuses
{
    public const string New = "new", InProgress = "in_progress", Answered = "answered", Closed = "closed";
    public static readonly string[] All = [New, InProgress, Answered, Closed];
    public static readonly Dictionary<string, string> Arabic = new() { [New] = "جديد", [InProgress] = "قيد المعالجة", [Answered] = "تم الرد", [Closed] = "مغلق" };
}

public static class TicketPriorities
{
    public const string Normal = "normal", Urgent = "urgent";
    public static readonly string[] All = [Normal, Urgent];
}

public static class OtpPurposes
{
    public const string Login = "login", Phone = "phone", Email = "email", VerifyEmail = "verify_email", SignUp = "signup";
}

public static class PaymentPurposes
{
    public const string TopUp = "topup", Deposit = "deposit", Invoice = "invoice", BidDeposit = "bid_deposit", Listing = "listing", Transfer = "transfer", Violation = "violation";
    public static readonly string[] All = [TopUp, Deposit, Invoice, BidDeposit, Listing, Transfer, Violation];
}
