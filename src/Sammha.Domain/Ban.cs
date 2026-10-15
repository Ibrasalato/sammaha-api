namespace Sammha.Domain;

/// <summary>
/// «الحظر»: an owner barred from taking part on the platform for a period (or for good). A ban names an account, a national ID, or both;
/// a ban by national ID also catches an account that registers with that ID later. While a ban is in force the owner can still sign in,
/// see their data and pay what they owe, but cannot bid, buy, sell or send new requests.
/// </summary>
public class Ban
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string? NationalId { get; set; }
    /// <summary>the person's name as it was when banned</summary>
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
    /// <summary>Riyadh calendar days, both inclusive; no end = until lifted</summary>
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    /// <summary>manual | legacy_blacklist | legacy_excluded</summary>
    public string Source { get; set; } = BanSources.Manual;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTimeOffset? LiftedAt { get; set; }
    public string? LiftedByName { get; set; }
    public string? LiftReason { get; set; }

    public bool InForce(DateOnly today) => LiftedAt == null && StartsOn <= today && (EndsOn == null || EndsOn >= today);

    /// <summary>active | scheduled | ended | lifted</summary>
    public string StatusOn(DateOnly today) => LiftedAt != null ? BanStatuses.Lifted : StartsOn > today ? BanStatuses.Scheduled : EndsOn is { } e && e < today ? BanStatuses.Ended : BanStatuses.Active;
}

public static class BanSources
{
    public const string Manual = "manual", LegacyBlacklist = "legacy_blacklist", LegacyExcluded = "legacy_excluded";
}

public static class BanStatuses
{
    public const string Active = "active", Scheduled = "scheduled", Ended = "ended", Lifted = "lifted";
    public static readonly string[] All = [Active, Scheduled, Ended, Lifted];

    /// <summary>what a banned owner cannot do: bid, buy, sell, and send new requests</summary>
    public static readonly HashSet<string> BlockedCommands =
    [
        "PlaceBidCommand", "SetAutoBidCommand", "PayBidDepositCommand", "BuyListingCommand", "CreateListingCommand",
        "CreateRequestCommand", "CreateCompoundRequestCommand", "CreateInvestmentRequestCommand", "CreateTransferCommand", "RequestExemptionCommand",
    ];
}
