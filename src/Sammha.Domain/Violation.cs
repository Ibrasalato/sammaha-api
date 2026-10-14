namespace Sammha.Domain;

/// <summary>A kind of violation the administration can issue to a camel owner, with its fine (0 = a penalty without a fine). Managed in the settings.</summary>
public class ViolationType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    /// <summary>the fine in riyals; 0 records the violation without anything to pay</summary>
    public decimal Amount { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>«تقبل التسوية بتسجيل اسم»: the owner may settle it by registering a name for the camel instead of paying</summary>
    public bool AllowsNameResolution { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A camel festival a violation can be issued at (managed in the settings next to the violation types).</summary>
public class Festival
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public int? Year { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// «المخالفات»: a fine or penalty issued to an owner. The type's name and amount are kept as they were when it was issued.
/// A fine is paid through the payment channels (card) only; fines are not subject to VAT, so no tax invoice is issued — the payment is the receipt.
/// </summary>
public class Violation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>VL-YYYY-NNNNN</summary>
    public string Number { get; set; } = "";
    /// <summary>the owner it was issued to</summary>
    public Guid UserId { get; set; }
    public Guid TypeId { get; set; }
    public string TypeName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    /// <summary>the festival it was issued at (name kept as it was)</summary>
    public Guid? FestivalId { get; set; }
    public string? FestivalName { get; set; }
    /// <summary>the chip number of the camel it is about</summary>
    public string? CamelChip { get; set; }
    /// <summary>optional evidence (PDF or image, up to 5 MB)</summary>
    public string? AttachmentName { get; set; }
    public string? AttachmentType { get; set; }
    public byte[]? AttachmentBytes { get; set; }
    public string Status { get; set; } = ViolationStatuses.Unpaid;
    public Guid IssuedById { get; set; }
    public string IssuedByName { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>the card payment that paid it (the receipt)</summary>
    public Guid? PaymentId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public string? CancelledByName { get; set; }
    /// <summary>how an unpaid violation was closed: paid_continue | resolved_name | banned (ViolationResolutions)</summary>
    public string? Resolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }
    /// <summary>staff's note on a name settlement, or the reason for a ban</summary>
    public string? ResolutionNote { get; set; }
}

public static class ViolationResolutions
{
    /// <summary>paid the fine and keeps taking part; registered a name for the camel; barred from the festival</summary>
    public const string PaidContinue = "paid_continue", ResolvedName = "resolved_name", Banned = "banned";
}

public static class ViolationStatuses
{
    /// <summary>unpaid: a fine waiting for payment; recorded: a penalty without a fine (nothing to pay)</summary>
    /// <summary>resolved_name: settled by registering a name (no payment); banned: barred from the festival. Paid, resolved_name and banned are final.</summary>
    public const string Unpaid = "unpaid", Paid = "paid", Recorded = "recorded", Cancelled = "cancelled", ResolvedName = "resolved_name", Banned = "banned";
    public static readonly string[] All = [Unpaid, Paid, Recorded, Cancelled, ResolvedName, Banned];
}
