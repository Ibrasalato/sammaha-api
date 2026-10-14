using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Finance;

public record PayoutStatusDto(bool Enabled, string Provider);
public record GetPayoutStatusQuery : IQuery<PayoutStatusDto>;
/// <summary>finance approves a refund request and the money goes straight to the owner's bank account (wallets.refund)</summary>
public record PayoutWithdrawalCommand(Guid Id) : ICommand<WithdrawalDto>;

public class PayoutHandlers(IAppDbContext db, Access access, IPayoutGateway gateway, PayoutEngine engine)
    : IRequestHandler<GetPayoutStatusQuery, PayoutStatusDto>, IRequestHandler<PayoutWithdrawalCommand, WithdrawalDto>
{
    public async Task<PayoutStatusDto> Handle(GetPayoutStatusQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.WalletsView, ct);
        return new(gateway.Enabled, gateway.Name);
    }

    public async Task<WithdrawalDto> Handle(PayoutWithdrawalCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.WalletsRefund, ct);
        if (!gateway.Enabled) throw new DomainException("التحويل المباشر غير مفعّل؛ نفّذ الحوالة من البنك وسجّل رقمها");
        var owner0 = await db.Withdrawals.AsNoTracking().Where(x => x.Id == c.Id).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("طلب الاسترداد غير موجود");
        await using var uow = await db.BeginAsync([$"u:{owner0}", $"w:{c.Id}"], ct);
        var w = await db.Withdrawals.FirstAsync(x => x.Id == c.Id, ct);
        if (w.Status != WithdrawalStatuses.Pending) throw new DomainException("تمت معالجة هذا الطلب");
        // the IBAN must still be the verified account the request was made to
        var bank = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == w.UserId, ct);
        if (bank == null || bank.Status != BankAccountStatuses.Verified || bank.Iban != w.Iban) throw new DomainException("الحساب البنكي للمالك غير معتمد أو تغيّر بعد الطلب");
        await engine.SendAsync(w, me.FullName, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        var name = await db.Users.Where(u => u.Id == w.UserId).Select(u => u.FullName).FirstAsync(ct);
        return FinanceHandlersDto.Withdrawal(w, name);
    }
}

public static class FinanceHandlersDto
{
    public static WithdrawalDto Withdrawal(Withdrawal w, string owner) =>
        new(w.Id, w.Number, w.UserId, owner, w.Amount, w.Iban, w.BankName, w.AccountName, w.Status, w.ByStaff, w.CreatedAt, w.ProcessedAt, w.ProcessedBy, w.TransferRef, w.Note,
            w.PayoutProvider, w.PayoutSentAt, w.PayoutError);
}

/// <summary>sends payouts and follows the ones the bank is still processing (from the sweeper)</summary>
public class PayoutEngine(IAppDbContext db, IPayoutGateway gateway, Notifier notifier, TimeProvider clock, ILogger<PayoutEngine> log)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private static string Sar(decimal v) => v.ToString("#,0.##", CultureInfo.InvariantCulture) + " ريال";

    public async Task SendAsync(Withdrawal w, string by, CancellationToken ct)
    {
        PayoutResult r;
        try { r = await gateway.SendAsync(w, ct); }
        catch (Exception e) when (e is not DomainException) { log.LogWarning(e, "payout {Number} failed", w.Number); r = new(PayoutResult.Failed, null, "تعذّر الاتصال ببوابة التحويل"); }
        w.PayoutProvider = gateway.Name;
        w.PayoutSentAt = Now;
        w.ProcessedBy = by;
        await ApplyAsync(w, r, ct);
    }

    private async Task ApplyAsync(Withdrawal w, PayoutResult r, CancellationToken ct)
    {
        switch (r.State)
        {
            case PayoutResult.Completed:
                w.Status = WithdrawalStatuses.Completed;
                w.TransferRef = r.Reference ?? w.PayoutRef;
                w.PayoutRef = r.Reference ?? w.PayoutRef;
                w.PayoutError = null;
                w.ProcessedAt = Now;
                await notifier.NotifyAsync(w.UserId, NotificationKinds.Refund, "حُوّل مبلغ الاسترداد إلى حسابك", $"{Sar(w.Amount)} إلى {w.BankName} {Iban.Mask(w.Iban)}، رقم العملية {w.TransferRef}.", "/dashboard/wallet", ct);
                break;
            case PayoutResult.Processing:
                w.Status = WithdrawalStatuses.Processing;
                w.PayoutRef = r.Reference ?? w.PayoutRef;
                w.PayoutError = null;
                break;
            default:
                // back to the queue: finance retries, transfers by hand or rejects (which returns the money to the wallet)
                w.Status = WithdrawalStatuses.Pending;
                w.PayoutError = string.IsNullOrWhiteSpace(r.Error) ? "رفضت بوابة التحويل العملية" : r.Error.Length > 300 ? r.Error[..300] : r.Error;
                break;
        }
    }

    public async Task PollAsync(CancellationToken ct)
    {
        if (!gateway.Enabled) return;
        var ids = await db.Withdrawals.Where(w => w.Status == WithdrawalStatuses.Processing).OrderBy(w => w.PayoutSentAt).Select(w => new { w.Id, w.UserId }).Take(50).ToListAsync(ct);
        foreach (var x in ids)
        {
            try
            {
                await using var uow = await db.BeginAsync([$"u:{x.UserId}", $"w:{x.Id}"], ct);
                var w = await db.Withdrawals.FirstAsync(y => y.Id == x.Id, ct);
                if (w.Status != WithdrawalStatuses.Processing) continue;
                await ApplyAsync(w, await gateway.StatusAsync(w, ct), ct);
                await db.SaveChangesAsync(ct);
                await uow.CommitAsync(ct);
            }
            catch (Exception e) { log.LogWarning(e, "payout status {Id}", x.Id); }
        }
    }
}
