using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Finance;

// ───────────────────────── contracts ─────────────────────────

public record BankAccountDto(string Iban, string IbanMasked, string BankName, string AccountName, string DocName, string DocType, string Status,
    string? RejectReason, DateTimeOffset UpdatedAt, DateTimeOffset? ReviewedAt, string? ReviewedBy);
public record BankAccountInput(string Iban, string AccountName, string? BankName, string? DocName, string? DocType, string? DocBase64);
public record DocumentDto(string Name, string ContentType, string Base64);
public record StaffBankAccountDto(Guid UserId, string OwnerName, string? Phone, BankAccountDto Account);

public record WithdrawalDto(Guid Id, string Number, Guid UserId, string OwnerName, decimal Amount, string Iban, string BankName, string AccountName,
    string Status, bool ByStaff, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt, string? ProcessedBy, string? TransferRef, string? Note);

public record WalletRowDto(Guid UserId, string OwnerName, string? Phone, decimal Balance, decimal TotalIn, decimal TotalOut, decimal PendingWithdrawals,
    int TxCount, DateTimeOffset? LastTxAt, string? BankStatus, string? BankName);
public record WalletDetailDto(WalletRowDto Wallet, List<WalletTxDto> Txs, List<WithdrawalDto> Withdrawals, BankAccountDto? Bank);

public record DepositRowDto(string Kind, Guid? RequestId, Guid? AuctionId, Guid UserId, string OwnerName, string Reference, string? AuctionNumber,
    string Names, string Type, decimal Amount, string State, string Context, DateTimeOffset At);

public record MarkRowDto(Guid AuctionId, string Number, string Name, string Type, string Kind, string? RequestNumber, string? ListingNumber, string OwnerName,
    string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal OpeningPrice, decimal Price, int BidCount, int Bidders, string? WinnerName,
    bool? WonByRequester, string? InvoiceNumber, string? InvoiceStatus, decimal? Vat, string? CertCode, int Extensions);

public record CheckIbanQuery(string Iban) : IQuery<IbanCheck>;
public record GetMyBankAccountQuery : IQuery<BankAccountDto?>;
public record SaveMyBankAccountCommand(BankAccountInput Input) : ICommand<BankAccountDto>;
public record GetMyBankDocumentQuery : IQuery<DocumentDto>;
public record GetMyWithdrawalsQuery : IQuery<List<WithdrawalDto>>;
public record RequestWithdrawalCommand(decimal Amount) : ICommand<WithdrawalDto>;

public record ListBankAccountsQuery(string? Status) : IQuery<List<StaffBankAccountDto>>;
public record GetBankDocumentQuery(Guid UserId) : IQuery<DocumentDto>;
public record ReviewBankAccountCommand(Guid UserId, bool Approve, string? Reason) : ICommand<StaffBankAccountDto>;
public record ListWalletsQuery : IQuery<List<WalletRowDto>>;
public record GetWalletDetailQuery(Guid UserId) : IQuery<WalletDetailDto>;
public record ListWithdrawalsQuery(string? Status) : IQuery<List<WithdrawalDto>>;
public record CompleteWithdrawalCommand(Guid Id, string TransferRef) : ICommand<WithdrawalDto>;
public record RejectWithdrawalCommand(Guid Id, string Reason) : ICommand<WithdrawalDto>;
public record DirectRefundCommand(Guid UserId, decimal Amount, string TransferRef, string? Note) : ICommand<WithdrawalDto>;
public record AdjustWalletCommand(Guid UserId, decimal Amount, string Reason) : ICommand<WalletRowDto>;
public record ListDepositsQuery(string? State) : IQuery<List<DepositRowDto>>;
public record RefundDepositCommand(string Kind, Guid? RequestId, Guid? AuctionId, Guid? UserId, string Reason) : ICommand<DepositRowDto>;
public record MarksReportQuery(string? Type, string? State, DateTimeOffset? From, DateTimeOffset? To) : IQuery<List<MarkRowDto>>;

// ───────────────────────── handlers ─────────────────────────

public class FinanceHandlers(IAppDbContext db, Access access, Money money, Notifier notifier, TimeProvider clock) :
    IRequestHandler<CheckIbanQuery, IbanCheck>, IRequestHandler<GetMyBankAccountQuery, BankAccountDto?>, IRequestHandler<SaveMyBankAccountCommand, BankAccountDto>,
    IRequestHandler<GetMyBankDocumentQuery, DocumentDto>, IRequestHandler<GetMyWithdrawalsQuery, List<WithdrawalDto>>, IRequestHandler<RequestWithdrawalCommand, WithdrawalDto>,
    IRequestHandler<ListBankAccountsQuery, List<StaffBankAccountDto>>, IRequestHandler<GetBankDocumentQuery, DocumentDto>, IRequestHandler<ReviewBankAccountCommand, StaffBankAccountDto>,
    IRequestHandler<ListWalletsQuery, List<WalletRowDto>>, IRequestHandler<GetWalletDetailQuery, WalletDetailDto>, IRequestHandler<ListWithdrawalsQuery, List<WithdrawalDto>>,
    IRequestHandler<CompleteWithdrawalCommand, WithdrawalDto>, IRequestHandler<RejectWithdrawalCommand, WithdrawalDto>, IRequestHandler<DirectRefundCommand, WithdrawalDto>,
    IRequestHandler<AdjustWalletCommand, WalletRowDto>, IRequestHandler<ListDepositsQuery, List<DepositRowDto>>, IRequestHandler<RefundDepositCommand, DepositRowDto>,
    IRequestHandler<MarksReportQuery, List<MarkRowDto>>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private const int MaxDocBytes = 5 * 1024 * 1024;
    private static string Sar(decimal v) => v.ToString("#,0.##", CultureInfo.InvariantCulture) + " ريال";
    private static string Clip(string? s, int n) => (s ?? "").Trim() is var t && t.Length > n ? t[..n] : (s ?? "").Trim();

    private static BankAccountDto Dto(BankAccount b, bool full) =>
        new(full ? b.Iban : Iban.Mask(b.Iban), Iban.Mask(b.Iban), b.BankName, b.AccountName, b.DocName, b.DocType, b.Status, b.RejectReason, b.UpdatedAt, b.ReviewedAt, b.ReviewedBy);

    private static WithdrawalDto Dto(Withdrawal w, string owner, bool full) =>
        new(w.Id, w.Number, w.UserId, owner, w.Amount, full ? w.Iban : Iban.Mask(w.Iban), w.BankName, w.AccountName, w.Status, w.ByStaff, w.CreatedAt, w.ProcessedAt, w.ProcessedBy, w.TransferRef, w.Note);

    private void Log(User by, string field, string oldValue, string newValue) =>
        db.SettingsChanges.Add(new SettingsChange { At = Now, UserId = by.Id, UserName = by.FullName, Field = field, OldValue = Clip(oldValue, 400), NewValue = Clip(newValue, 400) });

    /// <summary>the file is what it claims to be: PDF, PNG or JPEG by its first bytes</summary>
    private static string? Sniff(byte[] b) =>
        b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 ? "application/pdf"
        : b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg" : null;

    // ───── owner: bank account ─────

    public Task<IbanCheck> Handle(CheckIbanQuery q, CancellationToken ct) => Task.FromResult(Iban.Check(q.Iban));

    public async Task<BankAccountDto?> Handle(GetMyBankAccountQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var b = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == u.Id, ct);
        return b == null ? null : Dto(b, true);
    }

    public async Task<BankAccountDto> Handle(SaveMyBankAccountCommand c, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (u.Role != Roles.Owner) throw DomainException.Forbidden("الحساب البنكي لملاك الإبل");
        var i = c.Input;
        var check = Iban.Check(i.Iban);
        if (!check.Valid) throw new DomainException(check.Message);
        var name = Clip(i.AccountName, 120);
        if (name.Length < 3) throw new DomainException("اكتب اسم صاحب الحساب كما في شهادة الآيبان");
        var bank = check.BankName ?? Clip(i.BankName, 80);
        if (bank.Length < 2) throw new DomainException("اكتب اسم البنك");

        await using var uow = await db.BeginAsync([$"u:{u.Id}"], ct);
        var b = await db.BankAccounts.FirstOrDefaultAsync(x => x.UserId == u.Id, ct);
        byte[]? doc = null;
        if (!string.IsNullOrEmpty(i.DocBase64))
        {
            try { doc = Convert.FromBase64String(i.DocBase64.Contains(',') ? i.DocBase64[(i.DocBase64.IndexOf(',') + 1)..] : i.DocBase64); }
            catch (FormatException) { throw new DomainException("تعذّرت قراءة الملف، أعد رفعه"); }
            if (doc.Length > MaxDocBytes) throw new DomainException("حجم شهادة الآيبان أكبر من 5 ميجابايت");
        }
        else if (b == null) throw new DomainException("ارفع شهادة الآيبان (PDF أو صورة)");
        string? type = doc == null ? null : Sniff(doc) ?? throw new DomainException("شهادة الآيبان تكون PDF أو صورة PNG أو JPG");

        var changed = b == null || b.Iban != check.Iban || b.AccountName != name || doc != null;
        b ??= db.BankAccounts.Add(new BankAccount { UserId = u.Id }).Entity;
        b.Iban = check.Iban; b.BankName = bank; b.AccountName = name; b.UpdatedAt = Now;
        if (doc != null) { b.DocBytes = doc; b.DocType = type!; b.DocName = Clip(string.IsNullOrWhiteSpace(i.DocName) ? "iban-certificate" : Path.GetFileName(i.DocName), 120); }
        // any change goes back to finance for verification
        if (changed) { b.Status = BankAccountStatuses.Pending; b.RejectReason = null; b.ReviewedAt = null; b.ReviewedBy = null; }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(b, true);
    }

    public async Task<DocumentDto> Handle(GetMyBankDocumentQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var b = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == u.Id, ct) ?? throw DomainException.NotFound("لا يوجد حساب بنكي");
        return new DocumentDto(b.DocName, b.DocType, Convert.ToBase64String(b.DocBytes));
    }

    // ───── owner: refunds to the bank ─────

    public async Task<List<WithdrawalDto>> Handle(GetMyWithdrawalsQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        return (await db.Withdrawals.AsNoTracking().Where(w => w.UserId == u.Id).OrderByDescending(w => w.CreatedAt).Take(100).ToListAsync(ct))
            .Select(w => Dto(w, u.FullName, true)).ToList();
    }

    public async Task<WithdrawalDto> Handle(RequestWithdrawalCommand c, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (u.Role != Roles.Owner) throw DomainException.Forbidden("الاسترداد لملاك الإبل");
        var amount = Money.Round2(c.Amount);
        if (amount < 10) throw new DomainException("أقل مبلغ للاسترداد 10 ريال");
        await using var uow = await db.BeginAsync([$"u:{u.Id}"], ct);
        var b = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == u.Id, ct);
        if (b == null) throw new DomainException("أضف حسابك البنكي وشهادة الآيبان أولًا");
        if (b.Status != BankAccountStatuses.Verified) throw new DomainException("حسابك البنكي بانتظار اعتماد الإدارة المالية");
        if (await db.Withdrawals.CountAsync(w => w.UserId == u.Id && w.Status == WithdrawalStatuses.Pending, ct) >= 3) throw new DomainException("لديك 3 طلبات استرداد قيد التنفيذ");
        var w = new Withdrawal
        {
            Number = $"WD-{await db.NextNumberAsync("WithdrawalNo", ct)}", UserId = u.Id, Amount = amount, Iban = b.Iban, BankName = b.BankName,
            AccountName = b.AccountName, CreatedAt = Now,
        };
        await money.DebitAsync(u.Id, amount, TxKinds.Withdrawal, $"طلب استرداد {w.Number} إلى {b.BankName} {Iban.Mask(b.Iban)}", w.Id, ct, "المبلغ أكبر من رصيد المحفظة");
        db.Withdrawals.Add(w);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(w, u.FullName, true);
    }

    // ───── staff: bank accounts ─────

    public async Task<List<StaffBankAccountDto>> Handle(ListBankAccountsQuery q, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        if (!await access.CanAsync(me, Permissions.BanksVerify, ct) && !await access.CanAsync(me, Permissions.WalletsView, ct))
            throw DomainException.Forbidden("ليست لديك صلاحية «اعتماد الحسابات البنكية»");
        var full = await access.CanAsync(me, Permissions.BanksVerify, ct);
        var query = db.BankAccounts.AsNoTracking();
        if (!string.IsNullOrEmpty(q.Status) && q.Status != "all") query = query.Where(b => b.Status == q.Status);
        var list = await (from b in query join u in db.Users.AsNoTracking() on b.UserId equals u.Id orderby b.UpdatedAt descending
                          select new { b, u.FullName, u.Phone }).Take(2000).ToListAsync(ct);
        return list.Select(x => new StaffBankAccountDto(x.b.UserId, x.FullName, x.Phone, Dto(x.b, full))).ToList();
    }

    public async Task<DocumentDto> Handle(GetBankDocumentQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.BanksVerify, ct);
        var b = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == q.UserId, ct) ?? throw DomainException.NotFound("لا يوجد حساب بنكي");
        return new DocumentDto(b.DocName, b.DocType, Convert.ToBase64String(b.DocBytes));
    }

    public async Task<StaffBankAccountDto> Handle(ReviewBankAccountCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.BanksVerify, ct);
        await using var uow = await db.BeginAsync([$"u:{c.UserId}"], ct);
        var b = await db.BankAccounts.FirstOrDefaultAsync(x => x.UserId == c.UserId, ct) ?? throw DomainException.NotFound("لا يوجد حساب بنكي");
        var owner = await db.Users.FirstAsync(u => u.Id == c.UserId, ct);
        var reason = Clip(c.Reason, 300);
        if (!c.Approve && reason.Length < 3) throw new DomainException("اكتب سبب الرفض، يصل للمالك");
        b.Status = c.Approve ? BankAccountStatuses.Verified : BankAccountStatuses.Rejected;
        b.RejectReason = c.Approve ? null : reason;
        b.ReviewedAt = Now; b.ReviewedBy = me.FullName;
        Log(me, c.Approve ? "BankVerified" : "BankRejected", owner.FullName, $"{b.BankName} {Iban.Mask(b.Iban)}{(c.Approve ? "" : " · " + reason)}");
        await notifier.NotifyAsync(owner.Id, NotificationKinds.Refund, c.Approve ? "اعتُمد حسابك البنكي" : "لم يُعتمد حسابك البنكي",
            c.Approve ? "يمكنك الآن طلب استرداد رصيد محفظتك إلى حسابك." : $"السبب: {reason}. حدّث البيانات أو الشهادة من «المحفظة».", "/dashboard/wallet", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return new StaffBankAccountDto(owner.Id, owner.FullName, owner.Phone, Dto(b, true));
    }

    // ───── staff: wallets ─────

    private async Task<List<WalletRowDto>> WalletRowsAsync(Guid? only, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.WalletsView, ct);
        var full = await access.CanAsync(me, Permissions.OwnersFull, ct);
        var txq = db.WalletTxs.AsNoTracking();
        if (only != null) txq = txq.Where(t => t.UserId == only);
        var sums = await txq.GroupBy(t => t.UserId).Select(g => new
        {
            UserId = g.Key, Balance = g.Sum(t => t.Amount), In = g.Where(t => t.Amount > 0).Sum(t => (decimal?)t.Amount) ?? 0,
            Out = g.Where(t => t.Amount < 0).Sum(t => (decimal?)t.Amount) ?? 0, Count = g.Count(), Last = g.Max(t => t.CreatedAt),
        }).ToListAsync(ct);
        var ids = sums.Select(s => s.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Role == Roles.Owner).ToDictionaryAsync(u => u.Id, ct);
        var pending = await db.Withdrawals.AsNoTracking().Where(w => w.Status == WithdrawalStatuses.Pending && ids.Contains(w.UserId))
            .GroupBy(w => w.UserId).Select(g => new { g.Key, Sum = g.Sum(w => w.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var banks = await db.BankAccounts.AsNoTracking().Where(b => ids.Contains(b.UserId)).Select(b => new { b.UserId, b.Status, b.BankName }).ToDictionaryAsync(b => b.UserId, ct);
        return sums.Where(s => users.ContainsKey(s.UserId)).Select(s =>
        {
            var u = users[s.UserId];
            banks.TryGetValue(s.UserId, out var bk);
            return new WalletRowDto(u.Id, u.FullName, full ? u.Phone : Features.Admin.OwnersHandlers.Mask(u.Phone), Math.Round(s.Balance, 2), Math.Round(s.In, 2),
                Math.Round(-s.Out, 2), pending.GetValueOrDefault(s.UserId), s.Count, s.Last, bk?.Status, bk?.BankName);
        }).OrderByDescending(r => r.Balance).ToList();
    }

    public Task<List<WalletRowDto>> Handle(ListWalletsQuery q, CancellationToken ct) => WalletRowsAsync(null, ct);

    public async Task<WalletDetailDto> Handle(GetWalletDetailQuery q, CancellationToken ct)
    {
        var row = (await WalletRowsAsync(q.UserId, ct)).FirstOrDefault() ?? throw DomainException.NotFound("لا توجد حركات في هذه المحفظة");
        var me = await access.StaffAsync(ct);
        var full = await access.CanAsync(me, Permissions.WalletsRefund, ct);
        var txs = await db.WalletTxs.AsNoTracking().Where(t => t.UserId == q.UserId).OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync(ct);
        var ws = await db.Withdrawals.AsNoTracking().Where(w => w.UserId == q.UserId).OrderByDescending(w => w.CreatedAt).Take(100).ToListAsync(ct);
        var b = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == q.UserId, ct);
        return new WalletDetailDto(row, txs.Select(Mapper.Tx).ToList(), ws.Select(w => Dto(w, row.OwnerName, full)).ToList(), b == null ? null : Dto(b, full));
    }

    public async Task<List<WithdrawalDto>> Handle(ListWithdrawalsQuery q, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        if (!await access.CanAsync(me, Permissions.WalletsView, ct) && !await access.CanAsync(me, Permissions.WalletsRefund, ct))
            throw DomainException.Forbidden("ليست لديك صلاحية «تقرير المحافظ»");
        var full = await access.CanAsync(me, Permissions.WalletsRefund, ct);
        var query = db.Withdrawals.AsNoTracking();
        if (!string.IsNullOrEmpty(q.Status) && q.Status != "all") query = query.Where(w => w.Status == q.Status);
        var list = await (from w in query join u in db.Users.AsNoTracking() on w.UserId equals u.Id orderby w.CreatedAt descending
                          select new { w, u.FullName }).Take(2000).ToListAsync(ct);
        return list.Select(x => Dto(x.w, x.FullName, full)).ToList();
    }

    public async Task<WithdrawalDto> Handle(CompleteWithdrawalCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.WalletsRefund, ct);
        var reference = Clip(c.TransferRef, 60);
        if (reference.Length < 3) throw new DomainException("اكتب رقم الحوالة البنكية");
        var w = await db.Withdrawals.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("طلب الاسترداد غير موجود");
        await using var uow = await db.BeginAsync([$"u:{w.UserId}"], ct);
        await db.Entry(w).ReloadAsync(ct);
        if (w.Status != WithdrawalStatuses.Pending) throw new DomainException("تمت معالجة هذا الطلب");
        w.Status = WithdrawalStatuses.Completed; w.TransferRef = reference; w.ProcessedAt = Now; w.ProcessedBy = me.FullName;
        var owner = await db.Users.FirstAsync(u => u.Id == w.UserId, ct);
        Log(me, "WithdrawalCompleted", owner.FullName, $"{w.Number} · {Sar(w.Amount)} · حوالة {reference}");
        await notifier.NotifyAsync(w.UserId, NotificationKinds.Refund, "حُوّل مبلغ الاسترداد إلى حسابك", $"{Sar(w.Amount)} إلى {w.BankName} {Iban.Mask(w.Iban)}، رقم الحوالة {reference}.", "/dashboard/wallet", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(w, owner.FullName, true);
    }

    public async Task<WithdrawalDto> Handle(RejectWithdrawalCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.WalletsRefund, ct);
        var reason = Clip(c.Reason, 300);
        if (reason.Length < 3) throw new DomainException("اكتب سبب الرفض، يصل للمالك");
        var w = await db.Withdrawals.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("طلب الاسترداد غير موجود");
        await using var uow = await db.BeginAsync([$"u:{w.UserId}"], ct);
        await db.Entry(w).ReloadAsync(ct);
        if (w.Status != WithdrawalStatuses.Pending) throw new DomainException("تمت معالجة هذا الطلب");
        w.Status = WithdrawalStatuses.Rejected; w.Note = reason; w.ProcessedAt = Now; w.ProcessedBy = me.FullName;
        // the amount goes back to the wallet
        money.Add(w.UserId, w.Amount, TxKinds.Withdrawal, $"إعادة مبلغ طلب الاسترداد {w.Number} ({reason})", w.Id);
        var owner = await db.Users.FirstAsync(u => u.Id == w.UserId, ct);
        Log(me, "WithdrawalRejected", owner.FullName, $"{w.Number} · {Sar(w.Amount)} · {reason}");
        await notifier.NotifyAsync(w.UserId, NotificationKinds.Refund, "لم يُنفّذ طلب الاسترداد", $"أُعيد {Sar(w.Amount)} إلى محفظتك. السبب: {reason}", "/dashboard/wallet", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(w, owner.FullName, true);
    }

    public async Task<WithdrawalDto> Handle(DirectRefundCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.WalletsRefund, ct);
        var amount = Money.Round2(c.Amount);
        if (amount <= 0) throw new DomainException("اكتب مبلغًا صحيحًا");
        var reference = Clip(c.TransferRef, 60);
        if (reference.Length < 3) throw new DomainException("اكتب رقم الحوالة البنكية");
        await using var uow = await db.BeginAsync([$"u:{c.UserId}"], ct);
        var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == c.UserId && u.Role == Roles.Owner, ct) ?? throw DomainException.NotFound("المالك غير موجود");
        var b = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == c.UserId, ct);
        if (b is not { Status: BankAccountStatuses.Verified }) throw new DomainException("لا يوجد حساب بنكي معتمد لهذا المالك؛ اعتمد شهادة الآيبان أولًا");
        var w = new Withdrawal
        {
            Number = $"WD-{await db.NextNumberAsync("WithdrawalNo", ct)}", UserId = owner.Id, Amount = amount, Iban = b.Iban, BankName = b.BankName, AccountName = b.AccountName,
            Status = WithdrawalStatuses.Completed, ByStaff = true, CreatedAt = Now, ProcessedAt = Now, ProcessedBy = me.FullName, TransferRef = reference, Note = Clip(c.Note, 300),
        };
        await money.DebitAsync(owner.Id, amount, TxKinds.Withdrawal, $"استرداد {w.Number} إلى {b.BankName} {Iban.Mask(b.Iban)}، حوالة {reference}", w.Id, ct, "المبلغ أكبر من رصيد المحفظة");
        db.Withdrawals.Add(w);
        Log(me, "WithdrawalDirect", owner.FullName, $"{w.Number} · {Sar(amount)} · حوالة {reference}");
        await notifier.NotifyAsync(owner.Id, NotificationKinds.Refund, "حُوّل رصيد من محفظتك إلى حسابك البنكي", $"{Sar(amount)} إلى {b.BankName} {Iban.Mask(b.Iban)}، رقم الحوالة {reference}.", "/dashboard/wallet", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(w, owner.FullName, true);
    }

    public async Task<WalletRowDto> Handle(AdjustWalletCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.WalletsAdjust, ct);
        var amount = Money.Round2(c.Amount);
        if (amount == 0 || Math.Abs(amount) > 1_000_000) throw new DomainException("اكتب مبلغ التسوية (موجب للإضافة، سالب للخصم)");
        var reason = Clip(c.Reason, 200);
        if (reason.Length < 5) throw new DomainException("اكتب سبب التسوية بوضوح، يظهر في كشف المالك");
        await using (var uow = await db.BeginAsync([$"u:{c.UserId}"], ct))
        {
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == c.UserId && u.Role == Roles.Owner, ct) ?? throw DomainException.NotFound("المالك غير موجود");
            if (amount < 0) await money.DebitAsync(owner.Id, -amount, TxKinds.Adjustment, $"تسوية: {reason}", null, ct, "الخصم أكبر من رصيد المحفظة");
            else money.Add(owner.Id, amount, TxKinds.Adjustment, $"تسوية: {reason}");
            Log(me, "WalletAdjust", owner.FullName, $"{(amount > 0 ? "+" : "")}{Sar(amount)} · {reason}");
            await notifier.NotifyAsync(owner.Id, NotificationKinds.Refund, amount > 0 ? "أُضيف مبلغ إلى محفظتك" : "خُصم مبلغ من محفظتك", $"{Sar(Math.Abs(amount))}: {reason}", "/dashboard/wallet", ct);
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
        }
        return (await WalletRowsAsync(c.UserId, ct)).First();
    }

    // ───── staff: guarantees ─────

    public async Task<List<DepositRowDto>> Handle(ListDepositsQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.DepositsView, ct);
        var all = string.IsNullOrEmpty(q.State) || q.State == "all";
        var reqQ = db.Requests.AsNoTracking().Include(r => r.User).Include(r => r.Candidates).Where(r => r.DepositState != DepositStates.None && r.DepositAmount > 0);
        if (!all) reqQ = reqQ.Where(r => r.DepositState == q.State);
        var reqs = await reqQ.OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).Take(3000).ToListAsync(ct);
        var reqIds = reqs.Select(r => r.Id).ToList();
        var auctionNos = await db.Auctions.AsNoTracking().Where(a => a.RequestId != null && reqIds.Contains(a.RequestId.Value))
            .Select(a => new { a.RequestId, a.Number }).ToListAsync(ct);
        var rows = reqs.Select(r => new DepositRowDto("request", r.Id, null, r.UserId, r.User.FullName, r.Number,
            string.Join("، ", auctionNos.Where(a => a.RequestId == r.Id && a.Number != "").Select(a => a.Number)) is { Length: > 0 } n ? n : null,
            string.Join("، ", r.Candidates.Where(c => c.Status != CandidateStatuses.Withdrawn).OrderBy(c => c.Position).Select(c => c.Name)), r.Type, r.DepositAmount,
            r.DepositState, r.Kind == RequestKinds.Investment ? "رسوم دراسة استثمار" : r.Status, r.SubmittedAt ?? r.CreatedAt)).ToList();

        var bidQ = from d in db.BidDeposits.AsNoTracking()
                   join a in db.Auctions.AsNoTracking() on d.AuctionId equals a.Id
                   join u in db.Users.AsNoTracking() on d.UserId equals u.Id
                   join l in db.Listings.AsNoTracking() on a.ListingId equals (Guid?)l.Id into lj
                   from l in lj.DefaultIfEmpty()
                   select new { d, a.Number, a.Name, a.Type, a.Settled, a.CancelledAt, a.EndsAt, Owner = u.FullName, Listing = l != null ? l.Number : null };
        if (!all) { var st = q.State == DepositStates.Held ? BidDepositStates.Held : q.State; bidQ = bidQ.Where(x => x.d.State == st); }
        var bids = await bidQ.OrderByDescending(x => x.d.CreatedAt).Take(3000).ToListAsync(ct);
        rows.AddRange(bids.Select(x => new DepositRowDto("bid", null, x.d.AuctionId, x.d.UserId, x.Owner, x.Listing ?? "", x.Number == "" ? null : x.Number,
            x.Name, x.Type, x.d.Amount, x.d.State, x.CancelledAt != null ? "cancelled" : x.Settled ? "settled" : x.EndsAt <= Now ? "ended" : "open", x.d.CreatedAt)));
        return rows.OrderByDescending(r => r.At).ToList();
    }

    public async Task<DepositRowDto> Handle(RefundDepositCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.DepositsRefund, ct);
        var reason = Clip(c.Reason, 200);
        if (reason.Length < 3) throw new DomainException("اكتب سبب الاسترداد، يصل للمالك");
        if (c.Kind == "request")
        {
            var rid = c.RequestId ?? throw new DomainException("حدّد الطلب");
            var owner = await db.Requests.AsNoTracking().Where(r => r.Id == rid).Select(r => (Guid?)r.UserId).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("الطلب غير موجود");
            await using var uow = await db.BeginAsync([$"r:{rid}", $"u:{owner}"], ct);
            var r = await db.Requests.Include(x => x.User).Include(x => x.Candidates).FirstAsync(x => x.Id == rid, ct);
            if (r.Kind == RequestKinds.Investment) throw new DomainException("رسوم دراسة الاستثمار غير مستردة؛ للاستثناء استخدم تسوية المحفظة");
            if (r.DepositState != DepositStates.Held) throw new DomainException("هذا الضمان ليس محجوزًا (رُدّ أو خُصم من الفاتورة)");
            await money.RefundDepositAsync(r, reason, ct);
            Log(me, "DepositRefund", r.User.FullName, $"ضمان الطلب {r.Number} · {Sar(r.DepositAmount)} · {reason}");
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
            return new DepositRowDto("request", r.Id, null, r.UserId, r.User.FullName, r.Number, null, string.Join("، ", r.Candidates.Select(x => x.Name)), r.Type, r.DepositAmount, r.DepositState, r.Status, r.SubmittedAt ?? r.CreatedAt);
        }
        if (c.Kind == "bid")
        {
            var aid = c.AuctionId ?? throw new DomainException("حدّد المزاد");
            var uid = c.UserId ?? throw new DomainException("حدّد المالك");
            await using var uow = await db.BeginAsync([$"a:{aid}", $"u:{uid}"], ct);
            var d = await db.BidDeposits.FirstOrDefaultAsync(x => x.AuctionId == aid && x.UserId == uid, ct) ?? throw DomainException.NotFound("الضمان غير موجود");
            var a = await db.Auctions.FirstAsync(x => x.Id == aid, ct);
            if (d.State != BidDepositStates.Held) throw new DomainException("هذا الضمان ليس محجوزًا");
            if (!a.Settled && a.CancelledAt == null && a.LeaderId == uid) throw new DomainException("المالك متصدر المزاد حاليًا؛ لا يُرد ضمانه قبل انتهاء المزاد");
            d.State = BidDepositStates.Refunded;
            money.Add(uid, d.Amount, TxKinds.Refund, $"استرداد ضمان دخول مزاد «{a.Name}» ({reason})", a.Id);
            var owner = await db.Users.FirstAsync(u => u.Id == uid, ct);
            Log(me, "DepositRefund", owner.FullName, $"ضمان دخول {a.Number} «{a.Name}» · {Sar(d.Amount)} · {reason}");
            await notifier.NotifyAsync(uid, NotificationKinds.Refund, "أُعيد ضمان دخول المزاد إلى محفظتك", $"{Sar(d.Amount)} من مزاد «{a.Name}» ({reason}).", "/dashboard/wallet", ct);
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
            return new DepositRowDto("bid", null, a.Id, uid, owner.FullName, "", a.Number, a.Name, a.Type, d.Amount, d.State, a.Settled ? "settled" : "open", d.CreatedAt);
        }
        throw new DomainException("نوع الضمان غير معروف");
    }

    // ───── marks report ─────

    public async Task<List<MarkRowDto>> Handle(MarksReportQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.ReportsMarks, ct);
        var now = Now;
        var aq = db.Auctions.AsNoTracking();
        if (NameTypes.IsValid(q.Type)) aq = aq.Where(a => a.Type == q.Type);
        if (q.From != null) aq = aq.Where(a => a.EndsAt >= q.From);
        if (q.To != null) aq = aq.Where(a => a.StartsAt <= q.To);
        aq = q.State switch
        {
            "sold" => aq.Where(a => a.Settled && a.CancelledAt == null),
            "live" => aq.Where(a => !a.Settled && a.CancelledAt == null && a.PausedAt == null && a.StartsAt <= now && a.EndsAt > now),
            "scheduled" => aq.Where(a => !a.Settled && a.CancelledAt == null && a.StartsAt > now),
            "paused" => aq.Where(a => a.PausedAt != null && a.CancelledAt == null),
            "cancelled" => aq.Where(a => a.CancelledAt != null),
            _ => aq,
        };
        var list = await aq.OrderByDescending(a => a.EndsAt).Take(5000).ToListAsync(ct);
        var ids = list.Select(a => a.Id).ToList();
        var userIds = list.Select(a => a.RequesterId).Concat(list.Where(a => a.WinnerId != null).Select(a => a.WinnerId!.Value)).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var reqIds = list.Where(a => a.RequestId != null).Select(a => a.RequestId!.Value).Distinct().ToList();
        var reqNos = await db.Requests.AsNoTracking().Where(r => reqIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => (r.Number, r.Kind), ct);
        var lstIds = list.Where(a => a.ListingId != null).Select(a => a.ListingId!.Value).ToList();
        var lstNos = await db.Listings.AsNoTracking().Where(l => lstIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Number, ct);
        var invoices = (await db.Invoices.AsNoTracking().Where(i => i.AuctionId != null && ids.Contains(i.AuctionId.Value)).ToListAsync(ct))
            .GroupBy(i => i.AuctionId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IssuedAt).First());
        var bidders = await db.Bids.AsNoTracking().Where(b => ids.Contains(b.AuctionId)).GroupBy(b => b.AuctionId)
            .Select(g => new { g.Key, N = g.Select(b => b.UserId).Distinct().Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var certs = await db.ReservedNames.AsNoTracking().Where(r => r.AuctionId != null && ids.Contains(r.AuctionId.Value)).ToDictionaryAsync(r => r.AuctionId!.Value, r => r.CertCode, ct);

        return list.Select(a =>
        {
            var kind = a.ListingId != null ? "resale" : a.IsInvestment ? "investment" : "request";
            var status = a.CancelledAt != null ? "cancelled" : a.Settled ? "sold" : a.PausedAt != null ? "paused" : a.PhaseAt(now) == "scheduled" ? "scheduled" : a.PhaseAt(now) == "live" ? "live" : "settling";
            invoices.TryGetValue(a.Id, out var inv);
            return new MarkRowDto(a.Id, a.Number, a.Name, a.Type, kind, a.RequestId != null && reqNos.TryGetValue(a.RequestId.Value, out var rn) ? rn.Number : null,
                a.ListingId != null ? lstNos.GetValueOrDefault(a.ListingId.Value) : null, users.GetValueOrDefault(a.RequesterId, ""), status, a.StartsAt, a.EndsAt,
                a.OpeningPrice, a.FinalPrice ?? a.CurrentPrice, a.BidCount, bidders.GetValueOrDefault(a.Id), a.WinnerId != null ? users.GetValueOrDefault(a.WinnerId.Value) : null,
                a.WonByRequester, inv?.Number, inv?.Status, inv?.Vat, certs.GetValueOrDefault(a.Id), a.Extensions);
        }).ToList();
    }
}
