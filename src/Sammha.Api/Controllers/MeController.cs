using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features;

namespace Sammha.Api.Controllers;

/// <summary>Everything that belongs to the signed-in owner.</summary>
[ApiController, Route("api/me"), Authorize]
public class MeController(AuthService auth, WalletService wallet, RequestService requests, BiddingService bidding, ListingService listings,
    TicketService tickets, PaymentService payments) : ControllerBase
{
    // account
    [HttpPut("profile")] public Task<UserDto> Profile(ProfileBody b, CancellationToken ct) => auth.UpdateProfileAsync(b.AvatarUrl, b.AvatarSet, b.ShowAvatar, ct);
    [HttpPut("account")] public Task<UserDto> Account(AccountBody b, CancellationToken ct) => auth.UpdateAccountAsync(b.FullName, b.NationalId, ct);
    [HttpPost("email")] public Task<OtpChallengeDto> Email(EmailBody b, CancellationToken ct) => auth.RequestEmailChangeAsync(b.Email, ct);
    [HttpPost("email/confirm")] public Task<UserDto> ConfirmEmail(CodeBody b, CancellationToken ct) => auth.ConfirmEmailChangeAsync(b.Challenge, b.Code, ct);
    [HttpPost("phone")] public Task<OtpChallengeDto> Phone(PhoneBody b, CancellationToken ct) => auth.RequestPhoneChangeAsync(b.Phone, ct);
    [HttpPost("phone/confirm")] public Task<UserDto> ConfirmPhone(CodeBody b, CancellationToken ct) => auth.ConfirmPhoneChangeAsync(b.Challenge, b.Code, ct);

    [HttpGet("notifications")] public Task<List<NotificationDto>> Notifications(CancellationToken ct) => wallet.NotificationsAsync(ct);
    [HttpPost("notifications/read")] public async Task<IActionResult> Read(CancellationToken ct) { await wallet.MarkReadAsync(ct); return NoContent(); }
    [HttpGet("tickets")] public Task<List<TicketDto>> Tickets(CancellationToken ct) => tickets.MineAsync(ct);

    // money
    [HttpGet("wallet")] public Task<WalletDto> Wallet(CancellationToken ct) => wallet.WalletAsync(ct);
    [HttpPost("wallet/topup")] public Task<WalletDto> TopUp(TopUpBody b, CancellationToken ct) => payments.TopUpAsync(b.Amount, b.Callback ?? "", ct);
    [HttpGet("invoices")] public Task<List<InvoiceDto>> Invoices(CancellationToken ct) => wallet.MyInvoicesAsync(ct);
    [HttpPost("invoices/{id:guid}/pay")] public Task<InvoiceDto> PayInvoice(Guid id, CancellationToken ct) => wallet.PayInvoiceAsync(id, ct);

    // names and bids
    [HttpGet("names")] public Task<List<ReservedNameDto>> Names(CancellationToken ct) => wallet.MyNamesAsync(ct);
    [HttpGet("bids")] public Task<List<MyBidDto>> Bids(CancellationToken ct) => bidding.MyBidsAsync(ct);
    [HttpGet("watchlist")] public Task<List<AuctionDto>> Watchlist(CancellationToken ct) => bidding.WatchlistAsync(ct);

    // logo requests
    [HttpGet("requests")] public Task<List<RequestDto>> Requests(CancellationToken ct) => requests.MineAsync(ct);
    [HttpPost("requests")] public Task<RequestDto> Create(NamesBody b, CancellationToken ct) => requests.CreateAsync(b.Type, b.Names, ct);
    [HttpPost("requests/compound")] public Task<RequestDto> Compound(CompoundBody b, CancellationToken ct) => requests.CreateCompoundAsync(b.ReservedId, b.FullName, ct);
    [HttpPost("requests/investment")] public Task<RequestDto> Investment(NamesBody b, CancellationToken ct) => requests.CreateInvestmentAsync(b.Type, b.Names, ct);
    [HttpGet("investments")] public Task<List<InvestmentItemDto>> Investments(CancellationToken ct) => requests.MyInvestmentsAsync(ct);
    [HttpPost("requests/{id:guid}/pay")] public Task<RequestDto> Pay(Guid id, CancellationToken ct) => requests.PayDepositAsync(id, ct);

    [HttpPost("requests/{id:guid}/cancel")]
    public async Task<object> Cancel(Guid id, ReasonBody b, CancellationToken ct) => new { refunded = await requests.CancelMineAsync(id, b.Reason, ct) };

    [HttpPost("requests/{id:guid}/withdraw")]
    public async Task<object> Withdraw(Guid id, WithdrawBody b, CancellationToken ct)
    {
        var (refunded, cancelled) = await requests.WithdrawNameAsync(id, b.CandidateId, ct);
        return new { refunded, requestCancelled = cancelled };
    }

    // «بيع اسم»
    [HttpGet("listings")] public Task<List<ListingDto>> Listings(CancellationToken ct) => listings.MineAsync(ct);
    [HttpPost("listings")] public Task<ListingDto> List(ListingBody b, CancellationToken ct) => listings.CreateAsync(b.ReservedId, b.Mode, b.Price, ct);
    [HttpPost("listings/{id:guid}/cancel")] public Task<ListingDto> CancelListing(Guid id, CancellationToken ct) => listings.CancelAsync(id, ct);
}

[ApiController, Route("api/requests"), Authorize]
public class RequestsController(RequestService requests) : ControllerBase
{
    /// <summary>the owner's own request, or any request for staff</summary>
    [HttpGet("{id:guid}")] public Task<RequestDto> Get(Guid id, CancellationToken ct) => requests.GetAsync(id, ct);
}

[ApiController, Route("api/auctions/{id:guid}"), Authorize]
public class BidsController(BiddingService bidding) : ControllerBase
{
    [HttpPost("bids")] public Task<AuctionViewDto> Bid(Guid id, AmountBody b, CancellationToken ct) => bidding.PlaceBidAsync(id, b.Amount, ct);
    [HttpPut("autobid")] public Task<AuctionViewDto> Auto(Guid id, MaxBody b, CancellationToken ct) => bidding.SetAutoBidAsync(id, b.Max, ct);
    [HttpDelete("autobid")] public Task<AuctionViewDto> CancelAuto(Guid id, CancellationToken ct) => bidding.CancelAutoBidAsync(id, ct);
    [HttpPost("watch")] public async Task<object> Watch(Guid id, CancellationToken ct) => new { watching = await bidding.ToggleWatchAsync(id, ct) };
    [HttpPost("deposit")] public Task<AuctionViewDto> Deposit(Guid id, CancellationToken ct) => bidding.PayBidDepositAsync(id, ct);
}
