using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Wallet;

/// <summary>Wallet, invoices, registered names and notifications of the signed-in owner.</summary>
public class WalletHandlers(IAppDbContext db, Access access, Mapper map, Money money)
    : IRequestHandler<GetWalletQuery, WalletDto>, IRequestHandler<GetMyInvoicesQuery, List<InvoiceDto>>, IRequestHandler<PayInvoiceCommand, InvoiceDto>, IRequestHandler<GetMyNamesQuery, List<ReservedNameDto>>, IRequestHandler<GetNotificationsQuery, List<NotificationDto>>, IRequestHandler<MarkNotificationsReadCommand, MediatR.Unit>
{
    private async Task<WalletDto> WalletAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var txs = await db.WalletTxs.AsNoTracking().Where(t => t.UserId == u.Id).OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync(ct);
        return new WalletDto(await db.BalanceOfAsync(u.Id, ct), txs.Select(Mapper.Tx).ToList());
    }

    private async Task<List<InvoiceDto>> MyInvoicesAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.Invoices.AsNoTracking().Where(i => i.UserId == u.Id).OrderByDescending(i => i.IssuedAt).ToListAsync(ct);
        return await map.InvoicesAsync(list, ct);
    }

    private async Task<InvoiceDto> PayInvoiceAsync(Guid id, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"u:{u.Id}", $"i:{id}"], ct);
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.UserId == u.Id, ct) ?? throw DomainException.NotFound("الفاتورة غير موجودة");
        if (inv.Status == InvoiceStatuses.Paid) return await map.InvoiceAsync(inv, ct);
        if (inv.Status == InvoiceStatuses.Cancelled) throw new DomainException("أُلغيت هذه الفاتورة لانتهاء مهلة السداد");
        await money.DebitByCardAsync(u.Id, inv.AmountDue, PaymentPurposes.Invoice, inv.Id, TxKinds.InvoicePayment, $"سداد طلب السداد {inv.Number} — {inv.Name}", ct);
        await money.MarkInvoicePaidAsync(inv, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.InvoiceAsync(inv, ct);
    }

    private async Task<List<ReservedNameDto>> MyNamesAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.ReservedNames.AsNoTracking().Where(r => r.OwnerId == u.Id).OrderByDescending(r => r.AcquiredAt).ToListAsync(ct);
        var ids = list.Select(r => r.Id).ToList();
        var active = await db.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.ReservedId) && (l.Status == ListingStatuses.Scheduled || l.Status == ListingStatuses.Live))
            .ToDictionaryAsync(l => l.ReservedId, l => l.Id, ct);
        var logos = await db.Logos.AsNoTracking().Where(l => ids.Contains(l.ReservedId)).ToDictionaryAsync(l => l.ReservedId, l => l.Status, ct);
        // «ربط بشريحة»: the owner's camel carrying each name
        var camels = (await db.OwnedCamels.AsNoTracking().Where(c => c.UserId == u.Id && c.ReservedId != null && ids.Contains(c.ReservedId.Value)).ToListAsync(ct))
            .GroupBy(c => c.ReservedId!.Value).ToDictionary(g => g.Key, g => g.First());
        return list.Select(r =>
        {
            var dto = Mapper.Reserved(r, null, active.TryGetValue(r.Id, out var lid) ? lid : null, logos.GetValueOrDefault(r.Id));
            return camels.TryGetValue(r.Id, out var c) ? dto with { CamelId = c.Id, CamelChip = Features.Camels.CamelLinks.Chips(c).FirstOrDefault() ?? "" } : dto;
        }).ToList();
    }

    private async Task<List<NotificationDto>> NotificationsAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        return (await db.Notifications.AsNoTracking().Where(n => n.UserId == u.Id).OrderByDescending(n => n.CreatedAt).Take(30).ToListAsync(ct))
            .Select(Mapper.Notification).ToList();
    }

    private async Task MarkReadAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await db.Notifications.Where(n => n.UserId == u.Id && !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true), ct);
    }

    // ───── MediatR entry points ─────
    public Task<WalletDto> Handle(GetWalletQuery q, CancellationToken ct) => WalletAsync(ct);
    public Task<List<InvoiceDto>> Handle(GetMyInvoicesQuery q, CancellationToken ct) => MyInvoicesAsync(ct);
    public Task<InvoiceDto> Handle(PayInvoiceCommand q, CancellationToken ct) => PayInvoiceAsync(q.InvoiceId, ct);
    public Task<List<ReservedNameDto>> Handle(GetMyNamesQuery q, CancellationToken ct) => MyNamesAsync(ct);
    public Task<List<NotificationDto>> Handle(GetNotificationsQuery q, CancellationToken ct) => NotificationsAsync(ct);
    public async Task<MediatR.Unit> Handle(MarkNotificationsReadCommand q, CancellationToken ct) { await MarkReadAsync(ct); return Unit.Value; }
}
