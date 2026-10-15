namespace Sammha.Domain;

/// <summary>a record imported from the previous Sammha database: (kind, old id) → new id, so a re-run never duplicates</summary>
public class LegacyLink
{
    public string Kind { get; set; } = "";
    public int LegacyId { get; set; }
    public Guid NewId { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

public static class LegacyKinds
{
    public const string Member = "member", Order = "order", Candidate = "candidate", Bidding = "bidding", Bid = "bid", Bill = "bill",
        Name = "name", Compound = "compound", Notification = "notification", Wallet = "wallet", Transfer = "transfer", Logo = "logo", Ticket = "ticket", Exemption = "exemption", Ban = "ban", Excluded = "excluded";
}
