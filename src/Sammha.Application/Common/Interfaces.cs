using Microsoft.EntityFrameworkCore;
using Sammha.Domain;

namespace Sammha.Application.Common;

/// <summary>The persistence port. Implemented by Infrastructure (EF Core + SQL Server).</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<LogoRequest> Requests { get; }
    DbSet<Candidate> Candidates { get; }
    DbSet<Auction> Auctions { get; }
    DbSet<Bid> Bids { get; }
    DbSet<AutoBid> AutoBids { get; }
    DbSet<Watch> Watches { get; }
    DbSet<BidDeposit> BidDeposits { get; }
    DbSet<ReservedName> ReservedNames { get; }
    DbSet<WalletTx> WalletTxs { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<NameListing> Listings { get; }
    DbSet<CamelEvent> Events { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketMessage> TicketMessages { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<Payment> Payments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<PlatformSettings> Settings { get; }
    DbSet<SettingsChange> SettingsChanges { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    /// <summary>next value of a database sequence (RequestNo, InvoiceNo, ListingNo, TicketNo)</summary>
    Task<int> NextNumberAsync(string sequence, CancellationToken ct = default);
    /// <summary>wallet balance including transactions added but not yet saved</summary>
    Task<decimal> BalanceOfAsync(Guid userId, CancellationToken ct = default);
    /// <summary>
    /// Opens a transaction and takes exclusive locks on the given keys (e.g. "a:{auctionId}", "u:{userId}"),
    /// so bids on one auction or payments from one wallet never interleave.
    /// </summary>
    Task<IUnitOfWork> BeginAsync(IEnumerable<string> locks, CancellationToken ct = default);
}

public interface IUnitOfWork : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Work that must run only once the current transaction commits (webhooks, realtime pushes).</summary>
public interface IPostCommit
{
    void Enqueue(Func<Task> action);
}

public interface ICurrentUser
{
    Guid? Id { get; }
    /// <summary>the security stamp carried by the access token</summary>
    string? Stamp { get; }
    /// <summary>caller's IP and browser, recorded with refresh tokens</summary>
    string? Ip { get; }
    string? UserAgent { get; }
    /// <summary>the sign-in (refresh-token family) this access token belongs to</summary>
    Guid? SessionId { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ISmsSender
{
    /// <summary>sends a one-time code by SMS (phone) or email</summary>
    Task SendCodeAsync(string target, string code, string purpose);
}

/// <summary>Outgoing webhooks to n8n (notifications, ticket and invoice emails). Fire-and-forget.</summary>
public interface IWebhookPublisher
{
    Task PublishAsync(string url, object body);
}

public interface IPaymentGateway
{
    /// <summary>creates a hosted card checkout (Moyasar) and returns its URL</summary>
    Task<(string ProviderId, string Url)> CreateCheckoutAsync(Payment payment, string description, string callbackUrl, CancellationToken ct);
}

public record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenIssuer
{
    /// <summary>short-lived signed JWT (sub, role, stamp, jti)</summary>
    AccessToken Issue(User user, Guid sessionId);
    /// <summary>how long a refresh token lives</summary>
    TimeSpan RefreshLifetime { get; }
}

/// <summary>Live updates to browsers (SignalR hub /hubs/auctions in the API).</summary>
public interface IAuctionRealtime
{
    /// <summary>an auction's price, leader, end time or result changed: pushed to its page and to the auctions list</summary>
    Task ChangedAsync(Guid auctionId);
    /// <summary>a new in-app notification, pushed to that user's open sessions only</summary>
    Task NotifyAsync(NotificationDto notification);
}
