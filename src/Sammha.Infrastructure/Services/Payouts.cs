using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Infrastructure.Services;

/// <summary>Payouts:Provider = "hyperpay" (needs the HyperPay payout credentials) or "none" (finance transfers by hand)</summary>
public class PayoutOptions
{
    public string Provider { get; set; } = "none";
}

/// <summary>development and the demo: the transfer succeeds at once</summary>
public class SimulatedPayoutGateway : IPayoutGateway
{
    public string Name => "simulated";
    public bool Enabled => true;
    public Task<PayoutResult> SendAsync(Withdrawal w, CancellationToken ct) => Task.FromResult(new PayoutResult(PayoutResult.Completed, "SIM-" + w.Number, null));
    public Task<PayoutResult> StatusAsync(Withdrawal w, CancellationToken ct) => Task.FromResult(new PayoutResult(PayoutResult.Completed, w.PayoutRef, null));
}

/// <summary>no gateway: the dashboard keeps the manual «record the transfer reference» flow</summary>
public class ManualPayoutGateway(IOptions<PayoutOptions> opt) : IPayoutGateway
{
    public string Name => opt.Value.Provider is { Length: > 0 } p && p != "none" ? p : "manual";
    public bool Enabled => false;
    public Task<PayoutResult> SendAsync(Withdrawal w, CancellationToken ct) => throw new DomainException("التحويل المباشر غير مفعّل");
    public Task<PayoutResult> StatusAsync(Withdrawal w, CancellationToken ct) => Task.FromResult(new PayoutResult(PayoutResult.Processing, w.PayoutRef, null));
}
