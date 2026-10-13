using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features.Bidding;

namespace Sammha.Api.Controllers;

[ApiController, Route("api/auctions/{id:guid}"), Authorize]
public class BidsController(ISender mediator) : ControllerBase
{
    [HttpPost("bids")]
    public Task<AuctionViewDto> Bid(Guid id, AmountBody b, CancellationToken ct) => mediator.Send(new PlaceBidCommand(id, b.Amount), ct);

    [HttpPut("autobid")]
    public Task<AuctionViewDto> Auto(Guid id, MaxBody b, CancellationToken ct) => mediator.Send(new SetAutoBidCommand(id, b.Max), ct);

    [HttpDelete("autobid")]
    public Task<AuctionViewDto> CancelAuto(Guid id, CancellationToken ct) => mediator.Send(new CancelAutoBidCommand(id), ct);

    [HttpPost("watch")]
    public Task<WatchResult> Watch(Guid id, CancellationToken ct) => mediator.Send(new ToggleWatchCommand(id), ct);

    /// <summary>resale auctions: the participation guarantee</summary>
    [HttpPost("deposit")]
    public Task<AuctionViewDto> Deposit(Guid id, CancellationToken ct) => mediator.Send(new PayBidDepositCommand(id), ct);
}
