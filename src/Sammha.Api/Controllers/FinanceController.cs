using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Finance;
using Sammha.Domain;

namespace Sammha.Api.Controllers;

public record AmountBody(decimal Amount);
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
