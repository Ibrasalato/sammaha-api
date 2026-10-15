namespace Sammha.Domain;

/// <summary>
/// An owner lets another person manage their account: the delegate signs in to their own account and switches to the owner's,
/// limited to the granted scopes and the period. Linked to the delegate's account by national ID and mobile number.
/// </summary>
public class Delegation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    /// <summary>set once an account with the same national ID and mobile number exists (at creation or when the delegate signs up)</summary>
    public Guid? DelegateUserId { get; set; }
    public string FullName { get; set; } = "";
    public string NationalId { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>the delegate's capacity (صفة المفوض)</summary>
    public string Capacity { get; set; } = DelegationCapacities.Agent;
    public string? CapacityNote { get; set; }
    /// <summary>comma-separated scopes</summary>
    public string Scopes { get; set; } = "";
    public bool Permanent { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public IReadOnlyList<string> ScopeList() => Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public string StatusAt(DateTimeOffset now) =>
        RevokedAt != null ? DelegationStatuses.Revoked
        : !Permanent && EndsAt is { } e && now >= e ? DelegationStatuses.Expired
        : now < StartsAt ? DelegationStatuses.Scheduled
        : DelegateUserId == null ? DelegationStatuses.AwaitingAccount
        : DelegationStatuses.Active;
}

public static class DelegationStatuses
{
    public const string Active = "active", Scheduled = "scheduled", Expired = "expired", Revoked = "revoked", AwaitingAccount = "awaiting_account";
}

public static class DelegationCapacities
{
    public const string Agent = "agent", Family = "family", Employee = "employee", Partner = "partner", Other = "other";
    public static readonly Dictionary<string, string> Arabic = new()
    {
        [Agent] = "وكيل شرعي", [Family] = "فرد من العائلة", [Employee] = "موظف أو مدير أعمال", [Partner] = "شريك", [Other] = "أخرى",
    };
}

/// <summary>What a delegate may do on the owner's account. Viewing is always allowed; account, security, bank and delegation stay with the owner.</summary>
public static class DelegationScopes
{
    public const string Requests = "requests", Invest = "invest", Bids = "bids", Payments = "payments", Sales = "sales", Tickets = "tickets";
    public static readonly (string Key, string Label, string Description)[] Catalog =
    [
        (Requests, "طلبات الشعارات", "تقديم طلبات الشعارات والأسماء المركبة وطلبات الاستثناء من التسمية، ودفع ضمانها، وسحب اسم أو إلغاء الطلب"),
        (Invest, "استثمار الاسم", "تقديم طلبات استثمار الاسم ودفع رسوم الدراسة"),
        (Bids, "المزايدة والشراء", "المزايدة والمزايدة الآلية والمتابعة، وضمان دخول المزاد، وشراء الأسماء المعروضة"),
        (Payments, "المحفظة والفواتير", "شحن المحفظة وسداد الفواتير والمخالفات وغرامات الاستثناء"),
        (Sales, "بيع الأسماء", "عرض أسماء المالك للبيع بالمزاد أو بسعر ثابت وإلغاء العرض"),
        (Tickets, "الشكاوى والاستفسارات", "تقديم الشكاوى والاستفسارات ومتابعتها"),
    ];
    public static readonly string[] All = Catalog.Select(c => c.Key).ToArray();
    public static string Label(string k) => Catalog.FirstOrDefault(c => c.Key == k).Label ?? k;

    /// <summary>the scope each owner command needs; commands not listed are for the owner alone</summary>
    public static readonly Dictionary<string, string> ByCommand = new()
    {
        ["CreateRequestCommand"] = Requests, ["RequestExemptionCommand"] = Requests, ["CancelMyExemptionCommand"] = Requests, ["PayDepositCommand"] = Requests, ["CancelMyRequestCommand"] = Requests, ["WithdrawNameCommand"] = Requests, ["CreateCompoundRequestCommand"] = Requests,
        ["CreateInvestmentRequestCommand"] = Invest,
        ["PlaceBidCommand"] = Bids, ["SetAutoBidCommand"] = Bids, ["CancelAutoBidCommand"] = Bids, ["ToggleWatchCommand"] = Bids, ["PayBidDepositCommand"] = Bids, ["BuyListingCommand"] = Bids,
        ["PayInvoiceCommand"] = Payments, ["PayViolationCommand"] = Payments, ["PayExemptionCommand"] = Payments, ["TopUpCommand"] = Payments, ["StartCardPaymentCommand"] = Payments,
        ["CreateListingCommand"] = Sales, ["CancelListingCommand"] = Sales,
        ["SubmitTicketCommand"] = Tickets, ["FollowUpTicketCommand"] = Tickets,
    };
    /// <summary>always allowed in a delegated session</summary>
    public static readonly HashSet<string> Always = ["MarkNotificationsReadCommand", "SignOutCommand", "RefreshSessionCommand", "StopActingCommand"];
    /// <summary>reads a delegate cannot see</summary>
    public static readonly HashSet<string> HiddenQueries = ["GetMyBankAccountQuery", "GetMyBankDocumentQuery", "GetMySessionsQuery", "GetMySecurityQuery", "ListMyDelegationsQuery", "GetMyWithdrawalsQuery", "ListMyActivityQuery"];
}

/// <summary>One entry of the activity log: who did what, when, from where, and whether it worked.</summary>
public class ActivityLog
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorRole { get; set; } = "";
    /// <summary>set when a delegate acted on an owner's account</summary>
    public Guid? OnBehalfOfId { get; set; }
    public string? OnBehalfOfName { get; set; }
    public string Action { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Details { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
}
