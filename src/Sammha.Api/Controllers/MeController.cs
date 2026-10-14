using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sammha.Application.Common;
using Sammha.Application.Features.Auth;
using Sammha.Application.Features.Bidding;
using Sammha.Application.Features.Camels;
using Sammha.Application.Features.Listings;
using Sammha.Application.Features.Payments;
using Sammha.Application.Features.Requests;
using Sammha.Application.Features.Tickets;
using Sammha.Application.Features.Wallet;

namespace Sammha.Api.Controllers;

/// <summary>Everything that belongs to the signed-in owner.</summary>
[ApiController, Route("api/me"), Authorize]
public class MeController(ISender mediator) : ControllerBase
{
    // ───── account ─────
    [HttpPut("profile")]
    public Task<UserDto> Profile(ProfileBody b, CancellationToken ct) => mediator.Send(new UpdateProfileCommand(b.AvatarUrl, b.AvatarSet, b.ShowAvatar), ct);

    [HttpPut("account")]
    public Task<UserDto> Account(AccountBody b, CancellationToken ct) => mediator.Send(new UpdateAccountCommand(b.FullName, b.NationalId), ct);

    /// <summary>address and a second number, completed after sign-up</summary>
    [HttpPut("address")]
    public Task<UserDto> Address(AddressInput b, CancellationToken ct) => mediator.Send(new UpdateAddressCommand(b), ct);

    /// <summary>personal details (accounts made before they were required)</summary>
    [HttpPut("personal")]
    public Task<UserDto> Personal(PersonalInput b, CancellationToken ct) => mediator.Send(new UpdatePersonalCommand(b), ct);

    /// <summary>sends a code to the account's email; confirming it marks the email verified</summary>
    [HttpPost("email/verify"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> VerifyEmail(CancellationToken ct) => mediator.Send(new RequestEmailVerificationCommand(), ct);

    [HttpPost("email/verify/confirm"), EnableRateLimiting("auth")]
    public Task<UserDto> ConfirmVerifyEmail(CodeBody b, CancellationToken ct) => mediator.Send(new ConfirmEmailVerificationCommand(b.Challenge, b.Code), ct);

    [HttpPost("email"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> Email(EmailBody b, CancellationToken ct) => mediator.Send(new RequestEmailChangeCommand(b.Email), ct);

    [HttpPost("email/confirm"), EnableRateLimiting("auth")]
    public Task<UserDto> ConfirmEmail(CodeBody b, CancellationToken ct) => mediator.Send(new ConfirmEmailChangeCommand(b.Challenge, b.Code), ct);

    [HttpPost("phone"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> Phone(PhoneBody b, CancellationToken ct) => mediator.Send(new RequestPhoneChangeCommand(b.Phone), ct);

    [HttpPost("phone/confirm"), EnableRateLimiting("auth")]
    public Task<UserDto> ConfirmPhone(CodeBody b, CancellationToken ct) => mediator.Send(new ConfirmPhoneChangeCommand(b.Challenge, b.Code), ct);

    [HttpGet("notifications")]
    public Task<List<NotificationDto>> Notifications(CancellationToken ct) => mediator.Send(new GetNotificationsQuery(), ct);

    [HttpPost("notifications/read")]
    public async Task<IActionResult> Read(CancellationToken ct) { await mediator.Send(new MarkNotificationsReadCommand(), ct); return NoContent(); }

    [HttpGet("tickets")]
    public Task<List<TicketDto>> Tickets(CancellationToken ct) => mediator.Send(new GetMyTicketsQuery(), ct);

    // ───── money ─────
    [HttpGet("wallet")]
    public Task<WalletDto> Wallet(CancellationToken ct) => mediator.Send(new GetWalletQuery(), ct);

    [HttpPost("wallet/topup")]
    public Task<WalletDto> TopUp(TopUpBody b, CancellationToken ct) => mediator.Send(new TopUpCommand(b.Amount, b.Callback ?? ""), ct);

    [HttpGet("invoices")]
    public Task<List<InvoiceDto>> Invoices(CancellationToken ct) => mediator.Send(new GetMyInvoicesQuery(), ct);

    [HttpPost("invoices/{id:guid}/pay")]
    public Task<InvoiceDto> PayInvoice(Guid id, CancellationToken ct) => mediator.Send(new PayInvoiceCommand(id), ct);

    // ───── names and bids ─────
    [HttpGet("names")]
    public Task<List<ReservedNameDto>> Names(CancellationToken ct) => mediator.Send(new GetMyNamesQuery(), ct);

    /// <summary>«إبلي»: camels registered to the owner on the camel registry platform</summary>
    [HttpGet("camels")]
    public Task<MyCamelsDto> Camels(CancellationToken ct) => mediator.Send(new GetMyCamelsQuery(), ct);

    /// <summary>«ربط بشريحة»: one of the owner's قعدان or بكار names on this camel</summary>
    [HttpPost("camels/{id:guid}/link")]
    public Task<OwnedCamelDto> LinkCamel(Guid id, CamelLinkBody b, CancellationToken ct) => mediator.Send(new LinkCamelCommand(id, b.ReservedId), ct);

    [HttpDelete("camels/{id:guid}/link")]
    public Task<OwnedCamelDto> UnlinkCamel(Guid id, CancellationToken ct) => mediator.Send(new UnlinkCamelCommand(id), ct);

    [HttpGet("bids")]
    public Task<List<MyBidDto>> Bids(CancellationToken ct) => mediator.Send(new GetMyBidsQuery(), ct);

    [HttpGet("watchlist")]
    public Task<List<AuctionDto>> Watchlist(CancellationToken ct) => mediator.Send(new GetWatchlistQuery(), ct);

    // ───── logo requests, compound names, «استثمار الاسم» ─────
    [HttpGet("requests")]
    public Task<List<RequestDto>> Requests(CancellationToken ct) => mediator.Send(new GetMyRequestsQuery(), ct);

    [HttpPost("requests")]
    public Task<RequestDto> Create(NamesBody b, CancellationToken ct) => mediator.Send(new CreateRequestCommand(b.Type, b.Names), ct);

    [HttpPost("requests/compound")]
    public Task<RequestDto> Compound(CompoundBody b, CancellationToken ct) => mediator.Send(new CreateCompoundRequestCommand(b.ReservedId, b.FullName), ct);

    [HttpPost("requests/investment")]
    public Task<RequestDto> Investment(NamesBody b, CancellationToken ct) => mediator.Send(new CreateInvestmentRequestCommand(b.Type, b.Names), ct);

    [HttpGet("investments")]
    public Task<List<InvestmentItemDto>> Investments(CancellationToken ct) => mediator.Send(new GetMyInvestmentsQuery(), ct);

    [HttpPost("requests/{id:guid}/pay")]
    public Task<RequestDto> Pay(Guid id, CancellationToken ct) => mediator.Send(new PayDepositCommand(id), ct);

    [HttpPost("requests/{id:guid}/cancel")]
    public Task<CancelResult> Cancel(Guid id, ReasonBody b, CancellationToken ct) => mediator.Send(new CancelMyRequestCommand(id, b.Reason), ct);

    [HttpPost("requests/{id:guid}/withdraw")]
    public Task<WithdrawResult> Withdraw(Guid id, WithdrawBody b, CancellationToken ct) => mediator.Send(new WithdrawNameCommand(id, b.CandidateId), ct);

    // ───── «بيع اسم» ─────
    [HttpGet("listings")]
    public Task<List<ListingDto>> Listings(CancellationToken ct) => mediator.Send(new GetMyListingsQuery(), ct);

    [HttpPost("listings")]
    public Task<ListingDto> List(ListingBody b, CancellationToken ct) => mediator.Send(new CreateListingCommand(b.ReservedId, b.Mode, b.Price), ct);

    [HttpPost("listings/{id:guid}/cancel")]
    public Task<ListingDto> CancelListing(Guid id, CancellationToken ct) => mediator.Send(new CancelListingCommand(id), ct);
}
