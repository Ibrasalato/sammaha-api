namespace Sammha.Domain;

/// <summary>
/// «نقل ملكية اسم»: an owner hands a name they own to another registered owner (sale outside the platform, gift, inheritance…).
/// The recipient accepts, the committee approves, and only then the name changes hands.
/// </summary>
public class NameTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>NT-1001</summary>
    public string Number { get; set; } = "";
    public Guid ReservedId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = NameTypes.Qaadan;
    public Guid FromId { get; set; }
    public string FromName { get; set; } = "";
    public Guid ToId { get; set; }
    public string ToName { get; set; } = "";
    public string Reason { get; set; } = TransferReasons.Sale;
    public string? Note { get; set; }
    /// <summary>the transfer fee taken from the sender's wallet (returned if it does not go through)</summary>
    public decimal Fee { get; set; }
    public string Status { get; set; } = TransferStatuses.AwaitingRecipient;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public static class TransferStatuses
{
    public const string AwaitingRecipient = "awaiting_recipient", UnderReview = "under_review", Completed = "completed", Rejected = "rejected", Declined = "declined", Cancelled = "cancelled";
    public static readonly string[] Open = [AwaitingRecipient, UnderReview];
}

public static class TransferReasons
{
    public const string Sale = "sale", Gift = "gift", Inheritance = "inheritance", Other = "other";
    public static readonly string[] All = [Sale, Gift, Inheritance, Other];
}
