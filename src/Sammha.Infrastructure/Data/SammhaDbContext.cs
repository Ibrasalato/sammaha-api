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
    public DbSet<StaffRole> StaffRoles => Set<StaffRole>();
    public DbSet<TicketRoute> TicketRoutes => Set<TicketRoute>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<LegacyLink> LegacyLinks => Set<LegacyLink>();
    public DbSet<NameTransfer> Transfers => Set<NameTransfer>();
    public DbSet<NameLogo> Logos => Set<NameLogo>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<OwnedCamel> OwnedCamels => Set<OwnedCamel>();

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
        b.HasSequence<int>("AuctionNo").StartsAt(1001);
        b.HasSequence<int>("WithdrawalNo").StartsAt(1001);
        b.HasSequence<int>("CertificateNo").StartsAt(1);
        b.HasSequence<int>("TransferNo").StartsAt(1001);
        b.HasSequence<int>("PayRequestNo").StartsAt(1);

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Phone).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.FullNameEn).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(16);
            e.HasIndex(x => x.NationalId).IsUnique().HasFilter("[NationalId] IS NOT NULL");
            e.Property(x => x.NationalId).HasMaxLength(20);
            foreach (var n in new[] { nameof(User.FirstName), nameof(User.FatherName), nameof(User.GrandfatherName), nameof(User.FamilyName) }) e.Property(n).HasMaxLength(30);
            e.Property(x => x.Gender).HasMaxLength(6);
            e.Property(x => x.Nationality).HasMaxLength(2);
            e.Property(x => x.ResidenceCountry).HasMaxLength(2);
            e.Property(x => x.ResidenceCity).HasMaxLength(60);
            e.Property(x => x.AltPhone).HasMaxLength(16);
            e.Property(x => x.AddressCountry).HasMaxLength(2);
            e.Property(x => x.AddressCity).HasMaxLength(60);
            e.Property(x => x.District).HasMaxLength(80);
            e.Property(x => x.PostalCode).HasMaxLength(10);
            e.Property(x => x.Street).HasMaxLength(120);
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
            e.Property(x => x.LatinName).HasMaxLength(80);
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

        b.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.ProviderId);
            e.Property(x => x.Method).HasMaxLength(16);
            e.Property(x => x.CallbackUrl).HasMaxLength(1000);
            e.Property(x => x.Integrity).HasMaxLength(200);
            e.Property(x => x.ResultCode).HasMaxLength(40);
        });
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
            e.Property(x => x.TransferClosedNote).HasMaxLength(300);
            e.Property(x => x.TransferOpen).HasDefaultValue(true);
            e.Property(x => x.ComingSoon).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.ServiceTextsJson).HasColumnType("nvarchar(max)").HasDefaultValue("{}");
            e.Property(x => x.SupportEmail).HasMaxLength(160);
            e.Property(x => x.SupportPhone).HasMaxLength(30);
            e.Property(x => x.WhatsApp).HasMaxLength(30);
            e.Property(x => x.Announcement).HasMaxLength(400);
            e.Property(x => x.AnnouncementTone).HasMaxLength(10);
            e.Property(x => x.MaintenanceNote).HasMaxLength(300);
            e.Property(x => x.UpdatedBy).HasMaxLength(120);
            e.ToTable("PlatformSettings", t => t.HasCheckConstraint("CK_PlatformSettings_Single", "[Id] = 1"));
        });
        b.Entity<StaffRole>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Permissions).HasMaxLength(1000);
            // the built-in committee role, with what the committee could do before roles existed
            e.HasData(new StaffRole
            {
                Id = Permissions.CommitteeRoleId, Name = "لجنة الاعتماد", Description = "مراجعة الطلبات وفتح المزادات والرد على الشكاوى",
                Permissions = string.Join(',', Permissions.CommitteeDefault), IsSystem = true, CreatedAt = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero),
            });
        });
        b.Entity<User>().HasOne<StaffRole>().WithMany().HasForeignKey(u => u.StaffRoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<NameLogo>(e =>
        {
            e.HasIndex(x => x.ReservedId).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.FileName).HasMaxLength(120);
            e.Property(x => x.ContentType).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RejectReason).HasMaxLength(300);
            e.Property(x => x.ReviewedBy).HasMaxLength(120);
            e.Property(x => x.Bytes).HasColumnType("varbinary(max)");
        });
        b.Entity<NameTransfer>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.ReservedId);
            e.HasIndex(x => x.FromId);
            e.HasIndex(x => x.ToId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.FromName).HasMaxLength(120);
            e.Property(x => x.ToName).HasMaxLength(120);
            e.Property(x => x.Reason).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
        });
        b.Entity<LegacyLink>(e =>
        {
            e.HasKey(x => new { x.Kind, x.LegacyId });
            e.Property(x => x.Kind).HasMaxLength(20);
            e.HasIndex(x => x.NewId);
        });
        b.Entity<Certificate>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(24);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Scope).HasMaxLength(12);
            e.Property(x => x.OwnerNameAr).HasMaxLength(160);
            e.Property(x => x.OwnerNameEn).HasMaxLength(160);
            e.Property(x => x.NationalId).HasMaxLength(20);
            e.Property(x => x.Nationality).HasMaxLength(4);
            e.Property(x => x.Signature).HasMaxLength(40);
            e.Property(x => x.RevokedBy).HasMaxLength(120);
            e.Property(x => x.RevokeReason).HasMaxLength(300);
            e.Property(x => x.ItemsJson).HasColumnType("nvarchar(max)");
        });
        b.Entity<Delegation>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.NationalId).HasMaxLength(20);
            e.Property(x => x.Phone).HasMaxLength(16);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Capacity).HasMaxLength(20);
            e.Property(x => x.CapacityNote).HasMaxLength(100);
            e.Property(x => x.Scopes).HasMaxLength(200);
            e.Property(x => x.RevokedBy).HasMaxLength(120);
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => x.DelegateUserId);
            e.HasIndex(x => new { x.NationalId, x.Phone });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OwnedCamel>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ExternalId);
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.ChipNumbers).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.Sex).HasMaxLength(10);
            e.Property(x => x.Color).HasMaxLength(60);
            e.Property(x => x.Type).HasMaxLength(80);
            e.Property(x => x.Age).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(60);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ActivityLog>(e =>
        {
            e.Property(x => x.ActorName).HasMaxLength(120);
            e.Property(x => x.ActorRole).HasMaxLength(20);
            e.Property(x => x.OnBehalfOfName).HasMaxLength(120);
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.Details).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(300);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(200);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.Category, x.At });
            e.HasIndex(x => x.ActorId);
        });
        b.Entity<BankAccount>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.BankName).HasMaxLength(80);
            e.Property(x => x.AccountName).HasMaxLength(120);
            e.Property(x => x.DocName).HasMaxLength(120);
            e.Property(x => x.DocType).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RejectReason).HasMaxLength(300);
            e.Property(x => x.ReviewedBy).HasMaxLength(120);
            e.HasIndex(x => x.Status);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Withdrawal>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.BankName).HasMaxLength(80);
            e.Property(x => x.AccountName).HasMaxLength(120);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ProcessedBy).HasMaxLength(120);
            e.Property(x => x.TransferRef).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TicketRoute>(e =>
        {
            e.HasKey(x => new { x.Kind, x.UserId });
            e.Property(x => x.Kind).HasMaxLength(20);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Ticket>().HasIndex(x => new { x.AssigneeId, x.Status });
        b.Entity<Auction>(e =>
        {
            e.Property(x => x.PauseReason).HasMaxLength(300);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number);
        });
        b.Entity<PlatformSettings>(e =>
        {
            e.Property(x => x.ComplaintEmails).HasMaxLength(400);
            e.Property(x => x.ReportEmails).HasMaxLength(400);
            e.Property(x => x.InquiryEmails).HasMaxLength(400);
            e.Property(x => x.SuggestionEmails).HasMaxLength(400);
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
        if (sequence is not ("RequestNo" or "InvoiceNo" or "ListingNo" or "TicketNo" or "AuctionNo" or "WithdrawalNo" or "CertificateNo" or "TransferNo" or "PayRequestNo")) throw new ArgumentException("unknown sequence", nameof(sequence));
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
