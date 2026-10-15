namespace Sammha.Domain;

/// <summary>
/// «طلب استثناء المشاركة بدون اسم»: a camel may not take part in a festival without a registered name. The owner asks for an exemption for
/// one chip at one festival; staff set the fine for not naming it; the owner pays it by card, and an exemption document is issued,
/// signed so anyone can verify it from its QR code. The fine is not subject to VAT (like violations): the card payment is the receipt.
/// </summary>
public class Exemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>EX-YYYY-NNNNN</summary>
    public string Number { get; set; } = "";
    public Guid UserId { get; set; }
    /// <summary>the chip number of the camel taking part without a name</summary>
    public string CamelChip { get; set; } = "";
    /// <summary>qaadan (قعود) | bakkar (بكرة)</summary>
    public string CamelType { get; set; } = NameTypes.Qaadan;
    public Guid? FestivalId { get; set; }
    /// <summary>the festival's name as it was when the request was made</summary>
    public string FestivalName { get; set; } = "";
    /// <summary>«الشوط»: the race the camel takes part in, optional</summary>
    public string? Race { get; set; }
    public string? OwnerNote { get; set; }
    public string Status { get; set; } = ExemptionStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>the fine staff set for not naming the camel</summary>
    public decimal? Amount { get; set; }
    /// <summary>staff's note to the owner when setting the fine</summary>
    public string? StaffNote { get; set; }
    public DateTimeOffset? PricedAt { get; set; }
    public Guid? PricedById { get; set; }
    public string? PricedByName { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectReason { get; set; }
    public string? RejectedByName { get; set; }
    /// <summary>the owner withdrew the request before paying</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaymentId { get; set; }

    // ───── the exemption document (issued on payment) ─────
    /// <summary>the owner as they were when the document was issued</summary>
    public string? OwnerNameAr { get; set; }
    public string? OwnerNameEn { get; set; }
    public string? NationalId { get; set; }
    /// <summary>whole seconds, so the signed content reads back identically</summary>
    public DateTimeOffset? IssuedAt { get; set; }
    /// <summary>HMAC of the document's content, printed on it and carried in its QR code</summary>
    public string? Signature { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedByName { get; set; }
    public string? RevokeReason { get; set; }

    /// <summary>the content the signature covers</summary>
    public string SignedContent() =>
        string.Join('|', "EX1", Number, UserId.ToString("N"), OwnerNameAr ?? "", NationalId ?? "", CamelChip, CamelType, FestivalName, Race ?? "",
            (Amount ?? 0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), IssuedAt?.ToUnixTimeSeconds().ToString() ?? "");
}

public static class ExemptionStatuses
{
    /// <summary>
    /// pending: waiting for staff to set the fine; priced: the fine is set, waiting for the owner to pay; paid: the document is issued;
    /// rejected: refused by staff; cancelled: withdrawn by the owner. Paid, rejected and cancelled are final (a paid document can still be revoked).
    /// </summary>
    public const string Pending = "pending", Priced = "priced", Paid = "paid", Rejected = "rejected", Cancelled = "cancelled";
    public static readonly string[] All = [Pending, Priced, Paid, Rejected, Cancelled];
    public static readonly string[] Open = [Pending, Priced];
}
