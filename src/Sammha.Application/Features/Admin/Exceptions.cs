using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Admin;

/// <summary>
/// «استثناء اسم من المزاد» (requests.exception): an accepted name of a standard request is registered to the requester at once,
/// for a registration fee instead of an auction. The request's guarantee goes towards the fee; any surplus returns to the wallet.
/// </summary>
public record GrantExceptionCommand(Guid CandidateId, decimal Fee, string Reason) : ICommand<RequestDto>;

public class ExceptionHandlers(IAppDbContext db, Access access, Mapper map, Money money, Notifier notifier, TimeProvider clock)
    : IRequestHandler<GrantExceptionCommand, RequestDto>
{
    public async Task<RequestDto> Handle(GrantExceptionCommand q, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.RequestsException, ct);
        var reason = (q.Reason ?? "").Trim();
        if (reason.Length < 5) throw new DomainException("اكتب سبب الاستثناء");
        if (q.Fee < 0 || q.Fee > 10_000_000) throw new DomainException("رسوم التسجيل غير صحيحة");
        var c0 = await db.Candidates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.CandidateId, ct) ?? throw DomainException.NotFound("الاسم غير موجود");
        await using var uow = await db.BeginAsync([$"r:{c0.RequestId}", "schedule"], ct);
        var r = await db.Requests.Include(x => x.Candidates).Include(x => x.User).FirstAsync(x => x.Id == c0.RequestId, ct);
        var c = r.Candidates.First(x => x.Id == q.CandidateId);
        if (r.Kind != RequestKinds.Standard) throw new DomainException("الاستثناء لطلبات الشعارات العادية فقط");
        if (r.Status != RequestStatuses.Accepted || c.Status != CandidateStatuses.Accepted) throw new DomainException("الاستثناء لاسم مقبول فقط");
        if (c.AuctionId != null) throw new DomainException("حُدّد لهذا الاسم مزاد، ألغِ المزاد أولًا");
        if (c.ExceptedAt != null) throw new DomainException("سُجّل هذا الاسم استثناءً من قبل");
        if (await db.ReservedNames.AnyAsync(x => x.Normalized == c.Normalized, ct)) throw new DomainException("الاسم مسجل في السجل مسبقًا");

        var now = clock.GetUtcNow();
        var fee = Money.Round2(q.Fee);
        decimal applied = 0;
        if (r.DepositState == DepositStates.Held)
        {
            applied = Math.Min(r.DepositAmount, fee);
            r.DepositState = DepositStates.Applied;
            var surplus = Money.Round2(r.DepositAmount - applied);
            if (surplus > 0) money.Add(r.UserId, surplus, TxKinds.Refund, $"باقي ضمان الطلب {r.Number} بعد رسوم تسجيل «{c.Name}»", r.Id);
        }
        c.ExceptedAt = now; c.ExceptionFee = fee; c.ExceptionReason = reason.Length > 300 ? reason[..300] : reason; c.ExceptedBy = staff.FullName;
        var inv = await money.IssueInvoiceAsync(r.UserId, null, null, c.Name, r.Type, fee, applied, ct);
        var nid = Guid.NewGuid();
        db.ReservedNames.Add(new ReservedName
        {
            Id = nid, Name = c.Name, Normalized = c.Normalized, Type = r.Type, OwnerId = r.UserId, OwnerName = r.User.FullName, Price = fee,
            AcquiredAt = now, Source = ReservedSources.Exception, CertCode = ReservedName.CertFor(nid),
        });
        await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, $"سُجّل «{c.Name}» باسمك دون مزاد",
            inv.Status == InvoiceStatuses.Paid ? $"استثنته لجنة الاعتماد من المزاد برسوم {fee:0.##} ريال سُددت من ضمان الطلب." : $"استثنته لجنة الاعتماد من المزاد برسوم تسجيل {fee:0.##} ريال. سدّد الفاتورة {inv.Number} لإصدار الشهادة.",
            inv.Status == InvoiceStatuses.Paid ? "/dashboard/names" : "/dashboard/invoices", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.RequestAsync(r, ct);
    }
}
