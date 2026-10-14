using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sammha.Application.Common;
using Sammha.Domain;
using Sammha.Infrastructure.Services;

namespace Sammha.Infrastructure.Data;

public class SammhaDbContext : DbContext, IAppDbContext
{
    private readonly PostCommit? postCommit;

    public SammhaDbContext(DbContextOptions<SammhaDbContext> options, PostCommit? postCommit = null) : base(options)
    {
        this.postCommit = postCommit;
        if (postCommit != null) postCommit.InTransaction = () => Database.CurrentTransaction != null;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<LogoRequest> Requests => Set<LogoRequest>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<AutoBid> AutoBids => Set<AutoBid>();
    public DbSet<Watch> Watches => Set<Watch>();
    public DbSet<BidDeposit> BidDeposits => Set<BidDeposit>();
    public DbSet<ReservedName> ReservedNames => Set<ReservedName>();
    public DbSet<WalletTx> WalletTxs => Set<WalletTx>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<NameListing> Listings => Set<NameListing>();
    public DbSet<CamelEvent> Events => Set<CamelEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PlatformSettings> Settings => Set<PlatformSettings>();
    public DbSet<SettingsChange> SettingsChanges => Set<SettingsChange>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(12, 2);
        b.Properties<string>().HaveMaxLength(400);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<int>("RequestNo").StartsAt(1001);
        b.HasSequence<int>("InvoiceNo").StartsAt(1);
        b.HasSequence<int>("ListingNo").StartsAt(1001);
        b.HasSequence<int>("TicketNo").StartsAt(1);

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Phone).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(10);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.AvatarUrl).HasMaxLength(-1); // data URL from the profile page
        });

        b.Entity<LogoRequest>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Candidates).WithOne(x => x.Request).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
        });

        b.Entity<Candidate>(e =>
        {
            e.HasIndex(x => x.Normalized);
            e.Property(x => x.SimilarJson).HasMaxLength(-1);
        });

        b.Entity<Auction>(e =>
        {
            e.HasIndex(x => new { x.Settled, x.EndsAt });
            e.HasIndex(x => x.StartsAt);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<Bid>(e => e.HasIndex(x => new { x.AuctionId, x.Amount }));
        b.Entity<AutoBid>(e => e.HasKey(x => new { x.AuctionId, x.UserId }));
        b.Entity<Watch>(e => e.HasKey(x => new { x.AuctionId, x.UserId }));
        b.Entity<BidDeposit>(e => e.HasKey(x => new { x.AuctionId, x.UserId }));

        b.Entity<ReservedName>(e =>
        {
            e.HasIndex(x => x.Normalized).IsUnique();
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => x.CertCode);
            e.Property(x => x.Name).HasMaxLength(60);
        });

        b.Entity<WalletTx>(e => e.HasIndex(x => new { x.UserId, x.CreatedAt }));

        b.Entity<Invoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.AuctionId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<NameListing>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.ReservedId, x.Status });
            e.HasIndex(x => new { x.Mode, x.Status });
        });

        b.Entity<CamelEvent>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.DescriptionEn).HasMaxLength(2000);
        });

        b.Entity<Notification>(e => e.HasIndex(x => new { x.UserId, x.CreatedAt }));

        b.Entity<Ticket>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.Phone);
            e.Property(x => x.Message).HasMaxLength(3000);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TicketMessage>(e => e.Property(x => x.Body).HasMaxLength(3000));

        b.Entity<Payment>(e => e.HasIndex(x => x.ProviderId));
        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
        });
        b.Entity<User>().Property(x => x.SecurityStamp).HasMaxLength(32);
        b.Entity<User>(e =>
        {
            // existing accounts keep receiving every notification
            e.Property(x => x.NotifyOutbid).HasDefaultValue(true);
            e.Property(x => x.NotifyReminders).HasDefaultValue(true);
            e.Property(x => x.NotifyResults).HasDefaultValue(true);
            e.Property(x => x.NotifyRequests).HasDefaultValue(true);
        });
        b.Entity<PlatformSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RequestsClosedNote).HasMaxLength(300);
            e.Property(x => x.CompoundClosedNote).HasMaxLength(300);
            e.Property(x => x.SaleClosedNote).HasMaxLength(300);
            e.Property(x => x.InvestmentClosedNote).HasMaxLength(300);
            e.Property(x => x.SupportEmail).HasMaxLength(160);
            e.Property(x => x.SupportPhone).HasMaxLength(30);
            e.Property(x => x.WhatsApp).HasMaxLength(30);
            e.Property(x => x.Announcement).HasMaxLength(400);
            e.Property(x => x.AnnouncementTone).HasMaxLength(10);
            e.Property(x => x.MaintenanceNote).HasMaxLength(300);
            e.Property(x => x.UpdatedBy).HasMaxLength(120);
            e.ToTable("PlatformSettings", t => t.HasCheckConstraint("CK_PlatformSettings_Single", "[Id] = 1"));
        });
        b.Entity<SettingsChange>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(120);
            e.Property(x => x.Field).HasMaxLength(60);
            e.Property(x => x.OldValue).HasMaxLength(400);
            e.Property(x => x.NewValue).HasMaxLength(400);
            e.HasIndex(x => x.At);
        });
    }

    public async Task<int> NextNumberAsync(string sequence, CancellationToken ct = default)
    {
        if (sequence is not ("RequestNo" or "InvoiceNo" or "ListingNo" or "TicketNo")) throw new ArgumentException("unknown sequence", nameof(sequence));
        var conn = Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT NEXT VALUE FOR [{sequence}]";
        cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<decimal> BalanceOfAsync(Guid userId, CancellationToken ct = default)
    {
        var persisted = await WalletTxs.Where(t => t.UserId == userId).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        // transactions added in this unit of work but not saved yet
        var pending = ChangeTracker.Entries<WalletTx>().Where(e => e.State == EntityState.Added && e.Entity.UserId == userId).Sum(e => e.Entity.Amount);
        return Math.Round(persisted + pending, 2);
    }

    public async Task<IUnitOfWork> BeginAsync(IEnumerable<string> locks, CancellationToken ct = default)
    {
        var owns = Database.CurrentTransaction is null;
        var tx = owns ? await Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct) : Database.CurrentTransaction!;
        // sorted, so two operations taking the same locks can never deadlock
        foreach (var key in locks.Distinct().OrderBy(k => k, StringComparer.Ordinal))
            await Database.ExecuteSqlInterpolatedAsync(
                $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {key}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 50001, 'busy', 1;", ct);
        return new UnitOfWork(this, owns ? tx : null, postCommit);
    }

    private sealed class UnitOfWork(SammhaDbContext db, IDbContextTransaction? tx, PostCommit? postCommit) : IUnitOfWork
    {
        private bool _done;
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (tx == null) return; // joined an outer transaction: the outer one commits
            await tx.CommitAsync(ct);
            _done = true;
            if (postCommit != null) await postCommit.FlushAsync();
        }
        public async ValueTask DisposeAsync()
        {
            if (tx == null) return;
            if (!_done)
            {
                await tx.RollbackAsync();
                db.ChangeTracker.Clear();
                postCommit?.Discard();
            }
            await tx.DisposeAsync();
        }
    }
}
