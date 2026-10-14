using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Transfers;

public record TransferDto(Guid Id, string Number, Guid ReservedId, string Name, string Type, Guid FromId, string FromName, Guid ToId, string ToName,
    string Reason, string? Note, decimal Fee, string Status, DateTimeOffset CreatedAt, DateTimeOffset? RespondedAt, DateTimeOffset? ReviewedAt,
    string? ReviewerName, string? ReviewNote, DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt, string? FromPhone = null, string? ToPhone = null);

public record MyTransfersDto(List<TransferDto> Outgoing, List<TransferDto> Incoming, decimal Fee);
/// <summary>who a mobile number belongs to, as a masked name the sender can recognise</summary>
public record RecipientDto(string MaskedName);

public record GetMyTransfersQuery : IQuery<MyTransfersDto>;
public record FindRecipientQuery(string Phone) : IQuery<RecipientDto>;
public record CreateTransferCommand(Guid ReservedId, string Phone, string Reason, string? Note) : ICommand<TransferDto>;
public record CancelTransferCommand(Guid Id) : ICommand<TransferDto>;
/// <summary>the recipient accepts or declines</summary>
public record RespondTransferCommand(Guid Id, bool Accept) : ICommand<TransferDto>;
public record ListTransfersQuery(string? Status) : IQuery<List<TransferDto>>;
/// <summary>the committee approves (the name changes hands) or rejects (the fee is returned) — names.transfer</summary>
public record ReviewTransferCommand(Guid Id, bool Approve, string? Note) : ICommand<TransferDto>;

public class TransferHandlers(IAppDbContext db, Access access, ICurrentUser current, Notifier notifier, Money money, SettingsStore settings, TimeProvider clock)
    : IRequestHandler<GetMyTransfersQuery, MyTransfersDto>, IRequestHandler<FindRecipientQuery, RecipientDto>, IRequestHandler<CreateTransferCommand, TransferDto>,
      IRequestHandler<CancelTransferCommand, TransferDto>, IRequestHandler<RespondTransferCommand, TransferDto>,
      IRequestHandler<ListTransfersQuery, List<TransferDto>>, IRequestHandler<ReviewTransferCommand, TransferDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private const string Link = "/dashboard/transfers";

    public static TransferDto Dto(NameTransfer t, string? fromPhone = null, string? toPhone = null) => new(t.Id, t.Number, t.ReservedId, t.Name, t.Type, t.FromId, t.FromName,
        t.ToId, t.ToName, t.Reason, t.Note, t.Fee, t.Status, t.CreatedAt, t.RespondedAt, t.ReviewedAt, t.ReviewerName, t.ReviewNote, t.CompletedAt, t.CancelledAt, fromPhone, toPhone);

    /// <summary>ownership is the account holder's decision: delegates cannot move names</summary>
    private async Task<User> OwnerAsync(CancellationToken ct)
    {
        if (current.DelegationId != null) throw DomainException.Forbidden("نقل الملكية لصاحب الحساب فقط");
        var u = await access.MeAsync(ct);
        if (u.Role != Roles.Owner) throw DomainException.Forbidden("نقل الملكية لحسابات ملاك الإبل");
        return u;
    }

    private async Task<User> RecipientAsync(string phone, Guid me, CancellationToken ct)
    {
        var p = Phones.FromInput(phone) ?? throw new DomainException("رقم الجوال غير صحيح");
        var u = await db.Users.FirstOrDefaultAsync(x => x.Phone == p && x.Role == Roles.Owner, ct);
        if (u == null || !u.Active) throw new DomainException("لا يوجد مالك مسجل بهذا الرقم");
        if (u.Id == me) throw new DomainException("لا يمكن نقل الاسم إلى حسابك نفسه");
        return u;
    }

    public async Task<MyTransfersDto> Handle(GetMyTransfersQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.Transfers.AsNoTracking().Where(t => t.FromId == u.Id || t.ToId == u.Id).OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return new(list.Where(t => t.FromId == u.Id).Select(t => Dto(t)).ToList(), list.Where(t => t.ToId == u.Id).Select(t => Dto(t)).ToList(), await settings.TransferFeeAsync(ct));
    }

    public async Task<RecipientDto> Handle(FindRecipientQuery q, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        return new(NameMatcher.MaskName((await RecipientAsync(q.Phone, u.Id, ct)).FullName));
    }

    public async Task<TransferDto> Handle(CreateTransferCommand q, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        if (!TransferReasons.All.Contains(q.Reason)) throw new DomainException("اختر سبب النقل");
        var note = q.Note?.Trim();
        if (note is { Length: > 500 }) throw new DomainException("الملاحظة طويلة (500 حرف كحد أقصى)");
        if (q.Reason == TransferReasons.Other && string.IsNullOrWhiteSpace(note)) throw new DomainException("اكتب سبب النقل");
        var to = await RecipientAsync(q.Phone, u.Id, ct);
        await using var uow = await db.BeginAsync([$"n:{q.ReservedId}"], ct);
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == q.ReservedId && x.OwnerId == u.Id, ct) ?? throw new DomainException("الاسم غير مسجل باسمك");
        await EnsureFreeAsync(r, u.Id, ct);
        if (await db.Transfers.AnyAsync(t => t.ReservedId == r.Id && TransferStatuses.Open.Contains(t.Status), ct)) throw new DomainException("للاسم طلب نقل ملكية قائم");
        var fee = await settings.TransferFeeAsync(ct);
        var t = new NameTransfer
        {
            Number = $"NT-{await db.NextNumberAsync("TransferNo", ct)}", ReservedId = r.Id, Name = r.Name, Type = r.Type, FromId = u.Id, FromName = u.FullName,
            ToId = to.Id, ToName = to.FullName, Reason = q.Reason, Note = string.IsNullOrWhiteSpace(note) ? null : note, Fee = fee, CreatedAt = Now,
        };
        if (fee > 0) await money.DebitAsync(u.Id, fee, TxKinds.ServiceFee, $"رسوم نقل ملكية «{r.Name}» ({t.Number})", t.Id, ct, "رصيد المحفظة لا يكفي لرسوم نقل الملكية، اشحن المحفظة أولًا");
        db.Transfers.Add(t);
        await notifier.NotifyAsync(to.Id, NotificationKinds.Transfer, $"طلب نقل ملكية «{r.Name}» إليك", $"يرغب {u.FullName} في نقل ملكية الاسم إلى حسابك. وافق على الطلب أو ارفضه من صفحة نقل الملكية.", Link, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(t);
    }

    /// <summary>a name that is on sale, being auctioned or not yet paid for cannot change hands</summary>
    private async Task EnsureFreeAsync(ReservedName r, Guid ownerId, CancellationToken ct)
    {
        if (await db.Listings.AnyAsync(l => l.ReservedId == r.Id && (l.Status == ListingStatuses.Scheduled || l.Status == ListingStatuses.Live), ct)) throw new DomainException("الاسم معروض للبيع حاليًا، ألغِ العرض أولًا");
        if (await db.Auctions.AnyAsync(a => a.Name == r.Name && !a.Settled && a.CancelledAt == null, ct)) throw new DomainException("للاسم مزاد قائم حاليًا");
        if (await db.Invoices.AnyAsync(i => i.UserId == ownerId && i.Name == r.Name && i.Status == InvoiceStatuses.Unpaid, ct)) throw new DomainException("سدّد فاتورة الاسم أولًا");
    }

    private async Task<NameTransfer> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Transfers.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("طلب النقل غير موجود");

    private void RefundFee(NameTransfer t, string why)
    {
        if (t.Fee > 0) money.Add(t.FromId, t.Fee, TxKinds.Refund, $"استرداد رسوم نقل ملكية «{t.Name}» ({t.Number}) — {why}", t.Id);
    }

    public async Task<TransferDto> Handle(CancelTransferCommand q, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        await using var uow = await db.BeginAsync([$"t:{q.Id}"], ct);
        var t = await LoadAsync(q.Id, ct);
        if (t.FromId != u.Id) throw DomainException.NotFound("طلب النقل غير موجود");
        if (!TransferStatuses.Open.Contains(t.Status)) throw new DomainException("لا يمكن إلغاء هذا الطلب");
        t.Status = TransferStatuses.Cancelled;
        t.CancelledAt = Now;
        RefundFee(t, "ألغاه المالك");
        await notifier.NotifyAsync(t.ToId, NotificationKinds.Transfer, $"أُلغي طلب نقل ملكية «{t.Name}»", $"ألغى {t.FromName} طلب نقل الاسم إليك.", Link, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(t);
    }

    public async Task<TransferDto> Handle(RespondTransferCommand q, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        await using var uow = await db.BeginAsync([$"t:{q.Id}"], ct);
        var t = await LoadAsync(q.Id, ct);
        if (t.ToId != u.Id) throw DomainException.NotFound("طلب النقل غير موجود");
        if (t.Status != TransferStatuses.AwaitingRecipient) throw new DomainException("تم الرد على هذا الطلب");
        t.RespondedAt = Now;
        if (q.Accept)
        {
            t.Status = TransferStatuses.UnderReview;
            await notifier.NotifyAsync(t.FromId, NotificationKinds.Transfer, $"وافق {t.ToName} على نقل «{t.Name}»", "الطلب الآن لدى لجنة الاعتماد للمراجعة.", Link, ct);
        }
        else
        {
            t.Status = TransferStatuses.Declined;
            RefundFee(t, "رفضه المستلم");
            await notifier.NotifyAsync(t.FromId, NotificationKinds.Transfer, $"رفض {t.ToName} نقل «{t.Name}»", t.Fee > 0 ? "أُعيدت رسوم النقل إلى محفظتك." : "بقي الاسم مسجلًا باسمك.", Link, ct);
        }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(t);
    }

    public async Task<List<TransferDto>> Handle(ListTransfersQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.NamesTransfer, ct);
        var x = db.Transfers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Status)) x = x.Where(t => t.Status == q.Status);
        var list = await x.OrderByDescending(t => t.CreatedAt).Take(1000).ToListAsync(ct);
        var ids = list.SelectMany(t => new[] { t.FromId, t.ToId }).Distinct().ToList();
        var phones = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Phone, ct);
        return list.Select(t => Dto(t, phones.GetValueOrDefault(t.FromId), phones.GetValueOrDefault(t.ToId))).ToList();
    }

    public async Task<TransferDto> Handle(ReviewTransferCommand q, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.NamesTransfer, ct);
        var note = q.Note?.Trim();
        if (!q.Approve && string.IsNullOrWhiteSpace(note)) throw new DomainException("اكتب سبب الرفض");
        if (note is { Length: > 500 }) throw new DomainException("الملاحظة طويلة (500 حرف كحد أقصى)");
        var t0 = await db.Transfers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == q.Id, ct) ?? throw DomainException.NotFound("طلب النقل غير موجود");
        await using var uow = await db.BeginAsync([$"t:{q.Id}", $"n:{t0.ReservedId}"], ct);
        var t = await LoadAsync(q.Id, ct);
        if (t.Status != TransferStatuses.UnderReview) throw new DomainException(t.Status == TransferStatuses.AwaitingRecipient ? "الطلب بانتظار موافقة المستلم" : "تمت مراجعة هذا الطلب");
        t.ReviewedAt = Now;
        t.ReviewerId = staff.Id;
        t.ReviewerName = staff.FullName;
        t.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note;
        if (q.Approve)
        {
            var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == t.ReservedId, ct) ?? throw DomainException.NotFound("الاسم غير موجود");
            if (r.OwnerId != t.FromId) throw new DomainException("الاسم لم يعد مسجلًا باسم المالك");
            await EnsureFreeAsync(r, t.FromId, ct);
            var to = await db.Users.FirstOrDefaultAsync(x => x.Id == t.ToId, ct);
            if (to == null || !to.Active) throw new DomainException("حساب المستلم غير مفعّل");
            r.OwnerId = to.Id;
            r.OwnerName = to.FullName;
            r.Source = ReservedSources.Transfer;
            r.AcquiredAt = Now;
            t.Status = TransferStatuses.Completed;
            t.CompletedAt = Now;
            t.ToName = to.FullName;
            await notifier.NotifyAsync(t.ToId, NotificationKinds.Transfer, $"أصبح «{t.Name}» مسجلًا باسمك", "اعتمدت لجنة الاعتماد نقل الملكية، ويمكنك إصدار شهادة تسجيل الاسم الآن.", "/dashboard/names", ct);
            await notifier.NotifyAsync(t.FromId, NotificationKinds.Transfer, $"تم نقل ملكية «{t.Name}»", $"أصبح الاسم مسجلًا باسم {t.ToName}، وأي شهادة سابقة تشمله لم تعد سارية له.", Link, ct);
        }
        else
        {
            t.Status = TransferStatuses.Rejected;
            RefundFee(t, "رفضته لجنة الاعتماد");
            var body = $"السبب: {note}" + (t.Fee > 0 ? " — أُعيدت رسوم النقل إلى محفظتك." : "");
            await notifier.NotifyAsync(t.FromId, NotificationKinds.Transfer, $"رُفض طلب نقل ملكية «{t.Name}»", body, Link, ct);
            await notifier.NotifyAsync(t.ToId, NotificationKinds.Transfer, $"رُفض طلب نقل ملكية «{t.Name}»", $"السبب: {note}", Link, ct);
        }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(t);
    }
}
