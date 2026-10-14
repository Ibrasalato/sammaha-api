using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Finance;
using Sammha.Domain;

namespace Sammha.Api.Controllers;

public record TransferBody(string TransferRef);
public record ReviewBankBody(string? Reason);
public record DirectRefundBody(decimal Amount, string TransferRef, string? Note);
public record AdjustBody(decimal Amount, string Reason);
public record RefundDepositBody(string Kind, Guid? RequestId, Guid? AuctionId, Guid? UserId, string Reason);
public record WithdrawalReasonBody(string Reason);

/// <summary>The owner's bank account (IBAN check and certificate) and refunds of their wallet to it.</summary>
[ApiController, Route("api"), Authorize]
public class OwnerFinanceController(ISender mediator) : ControllerBase
{
    /// <summary>IBAN checker: country length, mod-97 check digits and, for Saudi IBANs, the bank</summary>
    [HttpGet("banks/check-iban")]
    public Task<IbanCheck> CheckIban([FromQuery] string iban, CancellationToken ct) => mediator.Send(new CheckIbanQuery(iban), ct);

    [HttpGet("me/bank-account")]
    public async Task<IActionResult> MyBank(CancellationToken ct) => await mediator.Send(new GetMyBankAccountQuery(), ct) is { } b ? Ok(b) : NoContent();

    /// <summary>IBAN, account holder and the IBAN certificate (PDF, PNG or JPG up to 5 MB, base64); any change waits for finance to verify it</summary>
    [HttpPut("me/bank-account"), RequestSizeLimit(8 * 1024 * 1024)]
    public Task<BankAccountDto> SaveBank(BankAccountInput b, CancellationToken ct) => mediator.Send(new SaveMyBankAccountCommand(b), ct);

    [HttpGet("me/bank-account/document")]
    public Task<DocumentDto> MyDocument(CancellationToken ct) => mediator.Send(new GetMyBankDocumentQuery(), ct);

    [HttpGet("me/withdrawals")]
    public Task<List<WithdrawalDto>> MyWithdrawals(CancellationToken ct) => mediator.Send(new GetMyWithdrawalsQuery(), ct);

    [HttpPost("me/withdrawals")]
    public Task<WithdrawalDto> Withdraw(AmountBody b, CancellationToken ct) => mediator.Send(new RequestWithdrawalCommand(b.Amount), ct);
}

/// <summary>Finance: bank accounts, wallets, refunds to the bank, guarantees, and the marks report. Every handler checks its own permission.</summary>
[ApiController, Route("api/admin"), Authorize(Policy = "staff")]
public class FinanceController(ISender mediator) : ControllerBase
{
    [HttpGet("bank-accounts")]
    public Task<List<StaffBankAccountDto>> Banks([FromQuery] string? status, CancellationToken ct) => mediator.Send(new ListBankAccountsQuery(status), ct);

    [HttpGet("bank-accounts/{userId:guid}/document")]
    public Task<DocumentDto> BankDocument(Guid userId, CancellationToken ct) => mediator.Send(new GetBankDocumentQuery(userId), ct);

    [HttpPost("bank-accounts/{userId:guid}/verify")]
    public Task<StaffBankAccountDto> Verify(Guid userId, CancellationToken ct) => mediator.Send(new ReviewBankAccountCommand(userId, true, null), ct);

    [HttpPost("bank-accounts/{userId:guid}/reject")]
    public Task<StaffBankAccountDto> RejectBank(Guid userId, ReviewBankBody b, CancellationToken ct) => mediator.Send(new ReviewBankAccountCommand(userId, false, b.Reason), ct);

    [HttpGet("wallets")]
    public Task<List<WalletRowDto>> Wallets(CancellationToken ct) => mediator.Send(new ListWalletsQuery(), ct);

    [HttpGet("wallets/{userId:guid}")]
    public Task<WalletDetailDto> Wallet(Guid userId, CancellationToken ct) => mediator.Send(new GetWalletDetailQuery(userId), ct);

    [HttpPost("wallets/{userId:guid}/refund")]
    public Task<WithdrawalDto> DirectRefund(Guid userId, DirectRefundBody b, CancellationToken ct) => mediator.Send(new DirectRefundCommand(userId, b.Amount, b.TransferRef, b.Note), ct);

    [HttpPost("wallets/{userId:guid}/adjust")]
    public Task<WalletRowDto> Adjust(Guid userId, AdjustBody b, CancellationToken ct) => mediator.Send(new AdjustWalletCommand(userId, b.Amount, b.Reason), ct);

    [HttpGet("withdrawals")]
    public Task<List<WithdrawalDto>> Withdrawals([FromQuery] string? status, CancellationToken ct) => mediator.Send(new ListWithdrawalsQuery(status), ct);

    [HttpPost("withdrawals/{id:guid}/complete")]
    public Task<WithdrawalDto> Complete(Guid id, TransferBody b, CancellationToken ct) => mediator.Send(new CompleteWithdrawalCommand(id, b.TransferRef), ct);

    [HttpPost("withdrawals/{id:guid}/reject")]
    public Task<WithdrawalDto> RejectWithdrawal(Guid id, WithdrawalReasonBody b, CancellationToken ct) => mediator.Send(new RejectWithdrawalCommand(id, b.Reason), ct);

    [HttpGet("deposits")]
    public Task<List<DepositRowDto>> Deposits([FromQuery] string? state, CancellationToken ct) => mediator.Send(new ListDepositsQuery(state), ct);

    [HttpPost("deposits/refund")]
    public Task<DepositRowDto> RefundDeposit(RefundDepositBody b, CancellationToken ct) => mediator.Send(new RefundDepositCommand(b.Kind, b.RequestId, b.AuctionId, b.UserId, b.Reason), ct);

    [HttpGet("reports/marks")]
    public Task<List<MarkRowDto>> Marks([FromQuery] string? type, [FromQuery] string? state, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) =>
        mediator.Send(new MarksReportQuery(type, state, from, to), ct);
}

public record DelegationBody(string FullName, string NationalId, string? PhoneCountry, string Phone, string Email, string Capacity, string? CapacityNote,
    List<string>? Scopes, bool Permanent, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);
public record ActAsBody(Guid DelegationId);

/// <summary>Account delegation: owners grant, delegates switch in and out.</summary>
[ApiController, Route("api"), Authorize]
public class DelegationController(ISender mediator) : ControllerBase
{
    [HttpGet("delegation-scopes"), AllowAnonymous]
    public object Scopes() => new
    {
        scopes = DelegationScopes.Catalog.Select(c => new { key = c.Key, label = c.Label, description = c.Description }),
        capacities = DelegationCapacities.Arabic.Select(c => new { key = c.Key, label = c.Value }),
    };

    [HttpGet("me/delegations")]
    public Task<List<Sammha.Application.Features.Delegations.DelegationDto>> Mine(CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.ListMyDelegationsQuery(), ct);

    [HttpPost("me/delegations")]
    public Task<Sammha.Application.Features.Delegations.DelegationDto> Add(DelegationBody b, CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.SaveDelegationCommand(null, Input(b)), ct);

    [HttpPut("me/delegations/{id:guid}")]
    public Task<Sammha.Application.Features.Delegations.DelegationDto> Edit(Guid id, DelegationBody b, CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.SaveDelegationCommand(id, Input(b)), ct);

    [HttpPost("me/delegations/{id:guid}/revoke")]
    public Task<Sammha.Application.Features.Delegations.DelegationDto> Revoke(Guid id, CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.RevokeDelegationCommand(id), ct);

    [HttpGet("me/delegated")]
    public Task<List<Sammha.Application.Features.Delegations.DelegationDto>> ToMe(CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.ListDelegatedToMeQuery(), ct);

    /// <summary>the owner's own activity: their actions, their delegates' actions on their account, and their actions as a delegate</summary>
    [HttpGet("me/activity")]
    public Task<Sammha.Application.Features.Admin.ActivityPage> Activity([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? category,
        [FromQuery] string? who, [FromQuery] string? q, [FromQuery] bool? failed, [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default) =>
        mediator.Send(new Sammha.Application.Features.Admin.ListMyActivityQuery(from, to, category, who, q, failed, page, size), ct);

    [HttpPost("auth/act-as")]
    public Task<Sammha.Application.Common.AuthResult> ActAs(ActAsBody b, CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.ActAsCommand(b.DelegationId), ct);

    [HttpPost("auth/act-as/stop")]
    public Task<Sammha.Application.Common.AuthResult> Stop(CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Delegations.StopActingCommand(), ct);

    private static Sammha.Application.Features.Delegations.DelegationInput Input(DelegationBody b) =>
        new(b.FullName, b.NationalId, b.PhoneCountry, b.Phone, b.Email, b.Capacity, b.CapacityNote, b.Scopes, b.Permanent, b.StartsAt, b.EndsAt);
}

/// <summary>The activity log (logs.view).</summary>
[ApiController, Route("api/admin/activity"), Authorize(Policy = "staff")]
public class ActivityController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public Task<Sammha.Application.Features.Admin.ActivityPage> List([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? category,
        [FromQuery] string? role, [FromQuery] string? q, [FromQuery] bool? failed, [FromQuery] int page = 1, [FromQuery] int size = 100, CancellationToken ct = default) =>
        mediator.Send(new Sammha.Application.Features.Admin.ListActivityQuery(from, to, category, role, q, failed, page, size), ct);
}
