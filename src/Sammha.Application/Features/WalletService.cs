using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features;

/// <summary>Wallet, invoices, registered names and notifications of the signed-in owner.</summary>
public class WalletService(IAppDbContext db, Access access, Mapper map, Money money)
{
    public async Task<WalletDto> WalletAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var txs = await db.WalletTxs.AsNoTracking().Where(t => t.UserId == u.Id).OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync(ct);
        return new WalletDto(await db.BalanceOfAsync(u.Id, ct), txs.Select(Mapper.Tx).ToList());
    }

    public async Task<List<InvoiceDto>> MyInvoicesAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.Invoices.AsNoTracking().Where(i => i.UserId == u.Id).OrderByDescending(i => i.IssuedAt).ToListAsync(ct);
        return await map.InvoicesAsync(list, ct);
    }

    public async Task<InvoiceDto> PayInvoiceAsync(Guid id, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"u:{u.Id}", $"i:{id}"], ct);
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.UserId == u.Id, ct) ?? throw DomainException.NotFound("الفاتورة غير موجودة");
        if (inv.Status == InvoiceStatuses.Paid) return await map.InvoiceAsync(inv, ct);
        await money.DebitAsync(u.Id, inv.AmountDue, TxKinds.InvoicePayment, $"سداد الفاتورة {inv.Number} — {inv.Name}", inv.Id, ct, "رصيد المحفظة لا يكفي");
        await money.MarkInvoicePaidAsync(inv, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.InvoiceAsync(inv, ct);
    }

    public async Task<List<ReservedNameDto>> MyNamesAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.ReservedNames.AsNoTracking().Where(r => r.OwnerId == u.Id).OrderByDescending(r => r.AcquiredAt).ToListAsync(ct);
        var ids = list.Select(r => r.Id).ToList();
        var active = await db.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.ReservedId) && (l.Status == ListingStatuses.Scheduled || l.Status == ListingStatuses.Live))
            .ToDictionaryAsync(l => l.ReservedId, l => l.Id, ct);
        return list.Select(r => Mapper.Reserved(r, null, active.TryGetValue(r.Id, out var lid) ? lid : null)).ToList();
    }

    public async Task<List<NotificationDto>> NotificationsAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        return (await db.Notifications.AsNoTracking().Where(n => n.UserId == u.Id).OrderByDescending(n => n.CreatedAt).Take(30).ToListAsync(ct))
            .Select(Mapper.Notification).ToList();
    }

    public async Task MarkReadAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await db.Notifications.Where(n => n.UserId == u.Id && !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true), ct);
    }
}
