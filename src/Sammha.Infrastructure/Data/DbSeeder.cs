using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Infrastructure.Data;

/// <summary>Applies migrations, then seeds the super admin, the events calendar and (Seed:Demo) sample data for development.</summary>
public class DbSeeder(SammhaDbContext db, IPasswordHasher hasher, IOptions<PlatformOptions> rules, IConfiguration cfg, TimeProvider clock, ILogger<DbSeeder> log)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync(ct);
        else await db.Database.EnsureCreatedAsync(ct);

        await SuperAdminAsync(ct);
        await EventsAsync(ct);
        if (cfg.GetValue<bool>("Seed:Demo")) await DemoAsync(ct);
        await AuctionNumbersAsync(ct);
    }

    /// <summary>auctions created before serial numbers get one, oldest first</summary>
    private async Task AuctionNumbersAsync(CancellationToken ct)
    {
        var missing = await db.Auctions.Where(a => a.Number == "").OrderBy(a => a.StartsAt).ToListAsync(ct);
        if (missing.Count == 0) return;
        foreach (var a in missing) a.Number = $"AU-{await db.NextNumberAsync("AuctionNo", ct)}";
        await db.SaveChangesAsync(ct);
    }

    private async Task SuperAdminAsync(CancellationToken ct)
    {
        var email = rules.Value.SuperAdminEmail;
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return;
        var password = cfg["SuperAdmin:InitialPassword"];
        if (string.IsNullOrEmpty(password))
        {
            password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
            log.LogWarning("SuperAdmin:InitialPassword is not set. A random password was generated for {Email}; set SuperAdmin:InitialPassword and recreate, or reset it in the database.", email);
        }
        db.Users.Add(new User
        {
            FullName = cfg["SuperAdmin:FullName"] ?? "إبراهيم صلاح", Phone = cfg["SuperAdmin:Phone"] ?? "0500000010", Email = email,
            Role = Roles.Admin, PasswordHash = hasher.Hash(password), Active = true, CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task EventsAsync(CancellationToken ct)
    {
        if (await db.Events.AnyAsync(ct)) return;
        await using var s = typeof(DbSeeder).Assembly.GetManifestResourceStream("events.seed.json")!;
        var list = await JsonSerializer.DeserializeAsync<List<CamelEvent>>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct) ?? [];
        db.Events.AddRange(list);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Same sample accounts as the front-end demo: owner 0500000001, committee 0500000002 (password 123456).</summary>
    private async Task DemoAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Phone == "0500000001", ct)) return;
        var now = clock.GetUtcNow();
        var hash = hasher.Hash("123456");
        User Mk(string name, string phone, string email, string role) =>
            new() { FullName = name, Phone = phone, Email = email, Role = role, PasswordHash = hash, Active = true, CreatedAt = now.AddDays(-90) };
        var owner = Mk("سعد بن ناصر العتيبي", "0500000001", "owner@demo.sa", Roles.Owner);
        var committee = Mk("لجنة الاعتماد", "0500000002", "committee@demo.sa", Roles.Committee);
        var b1 = Mk("مزرعة الوسم للإبل", "0500000003", "wasm@demo.sa", Roles.Owner);
        var b2 = Mk("فهد عبدالله الدوسري", "0500000004", "fahad@demo.sa", Roles.Owner);
        var b3 = Mk("نايف سالم المري", "0500000005", "nayef@demo.sa", Roles.Owner);
        db.Users.AddRange(owner, committee, b1, b2, b3);
        foreach (var u in new[] { owner, b1, b2, b3 })
            db.WalletTxs.Add(new WalletTx { UserId = u.Id, Amount = 2000, Kind = TxKinds.TopUp, Note = "رصيد تجريبي", CreatedAt = now.AddDays(-60) });

        (string Name, string Type, User Owner, decimal Price, int DaysAgo)[] legacy =
        [
            ("الفاخر", NameTypes.Qaadan, b1, 4200, 300), ("سلطان", NameTypes.Qaadan, b2, 12500, 260), ("مرهوب", NameTypes.Qaadan, b3, 3100, 240),
            ("شامخ", NameTypes.Qaadan, b1, 2750, 200), ("البيرق", NameTypes.Qaadan, b2, 8800, 180), ("الشعلة", NameTypes.Bakkar, b2, 9600, 280),
            ("الغيمة", NameTypes.Bakkar, b1, 5400, 250), ("مزنة", NameTypes.Bakkar, b3, 2300, 210), ("الهدباء", NameTypes.Bakkar, b1, 7100, 190),
            ("الوسمية", NameTypes.Manqiya, b1, 6400, 230), ("الحراير", NameTypes.Manqiya, b2, 3800, 170), ("الشرهات", NameTypes.Manqiya, owner, 2100, 95),
        ];
        foreach (var (name, type, o, price, days) in legacy)
        {
            var id = Guid.NewGuid();
            db.ReservedNames.Add(new ReservedName
            {
                Id = id, Name = name, Normalized = NameMatcher.Normalize(name), Type = type, OwnerId = o.Id, OwnerName = o.FullName, Price = price,
                AcquiredAt = now.AddDays(-days), Source = ReservedSources.Legacy, CertCode = ReservedName.CertFor(id),
            });
        }

        // one accepted request with a live auction and one scheduled
        var r = new LogoRequest
        {
            Number = $"SM-{await db.NextNumberAsync("RequestNo", ct)}", UserId = owner.Id, Type = NameTypes.Qaadan, Kind = RequestKinds.Standard,
            Status = RequestStatuses.Accepted, DepositAmount = rules.Value.DepositAmount, DepositPaid = true, DepositState = DepositStates.Held,
            CreatedAt = now.AddDays(-5), SubmittedAt = now.AddDays(-5), ReviewedAt = now.AddDays(-4), ReviewerName = committee.FullName,
        };
        var names = new[] { "الصارم", "المهيب", "الشامخ" };
        for (var i = 0; i < names.Length; i++)
            r.Candidates.Add(new Candidate { Position = i + 1, Name = names[i], Normalized = NameMatcher.Normalize(names[i]), Status = i < 2 ? CandidateStatuses.Accepted : CandidateStatuses.Rejected, RejectReason = i == 2 ? "مشابه لاسم مسجل مسبقًا" : null });
        db.Requests.Add(r);
        db.WalletTxs.Add(new WalletTx { UserId = owner.Id, Amount = -rules.Value.DepositAmount, Kind = TxKinds.Deposit, Note = $"ضمان مزاد الطلب {r.Number}", Ref = r.Id, CreatedAt = now.AddDays(-5) });
        var live = new Auction
        {
            RequestId = r.Id, CandidateId = r.Candidates[0].Id, Name = names[0], Type = r.Type, RequesterId = owner.Id, OpeningPrice = 50, MinIncrement = 10,
            StartsAt = now.AddDays(-1), EndsAt = now.AddHours(26), CurrentPrice = 260, BidCount = 2, LeaderId = b2.Id,
        };
        var soon = new Auction
        {
            RequestId = r.Id, CandidateId = r.Candidates[1].Id, Name = names[1], Type = r.Type, RequesterId = owner.Id, OpeningPrice = 50, MinIncrement = 10,
            StartsAt = now.AddDays(1), EndsAt = now.AddDays(3), CurrentPrice = 50,
        };
        r.Candidates[0].AuctionId = live.Id;
        r.Candidates[1].AuctionId = soon.Id;
        db.Auctions.AddRange(live, soon);
        db.Bids.Add(new Bid { AuctionId = live.Id, UserId = b1.Id, Amount = 120, CreatedAt = now.AddHours(-20) });
        db.Bids.Add(new Bid { AuctionId = live.Id, UserId = b2.Id, Amount = 260, CreatedAt = now.AddHours(-10) });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Demo data seeded");
    }
}
