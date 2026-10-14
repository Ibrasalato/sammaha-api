using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sammha.Application.Common;
using Sammha.Application.Features.Catalog;
using Sammha.Application.Features.Listings;
using Sammha.Application.Features.Tickets;

namespace Sammha.Api.Controllers;

/// <summary>Pages anyone can open.</summary>
[ApiController, Route("api")]
public class PublicController(ISender mediator) : ControllerBase
{
    [HttpGet("stats")]
    public Task<PublicStatsDto> Stats(CancellationToken ct) => mediator.Send(new GetStatsQuery(), ct);

    [HttpGet("auctions")]
    public Task<List<AuctionDto>> Auctions([FromQuery] string? phase, [FromQuery] string? type, [FromQuery] string? q, CancellationToken ct) =>
        mediator.Send(new ListAuctionsQuery(new AuctionFilter(phase, type, q)), ct);

    [HttpGet("auctions/{id:guid}")]
    public Task<AuctionViewDto> Auction(Guid id, CancellationToken ct) => mediator.Send(new GetAuctionQuery(id), ct);

    // the whole registry in one response: throttled per IP like the availability check
    [HttpGet("names"), EnableRateLimiting("check")]
    public Task<List<ReservedNameDto>> Names([FromQuery] string? q, [FromQuery] string? type, CancellationToken ct) => mediator.Send(new ListReservedNamesQuery(q, type), ct);

    [HttpPost("names/check"), EnableRateLimiting("check")]
    public Task<List<NameCheckDto>> Check(CheckBody b, CancellationToken ct) => mediator.Send(new CheckNamesQuery(b.Names), ct);

    [HttpGet("events")]
    public Task<List<CamelEventDto>> Events(CancellationToken ct) => mediator.Send(new ListEventsQuery(), ct);

    [HttpGet("certificates/{code}")]
    public Task<ReservedNameDto?> Verify(string code, CancellationToken ct) => mediator.Send(new VerifyCertificateQuery(code), ct);

    [HttpGet("market")]
    public Task<List<ListingDto>> Market(CancellationToken ct) => mediator.Send(new GetMarketQuery(), ct);

    [HttpPost("market/{id:guid}/buy"), Authorize]
    public Task<ListingDto> Buy(Guid id, CancellationToken ct) => mediator.Send(new BuyListingCommand(id), ct);

    [HttpPost("tickets"), EnableRateLimiting("auth"), RequestSizeLimit(64 * 1024)]
    public Task<TicketDto> Submit(TicketInput b, CancellationToken ct) => mediator.Send(new SubmitTicketCommand(b), ct);

    [HttpPost("tickets/track"), EnableRateLimiting("auth"), RequestSizeLimit(64 * 1024)]
    public Task<TicketDto?> Track(TrackBody b, CancellationToken ct) => mediator.Send(new TrackTicketQuery(b.Number, b.Phone), ct);

    [HttpPost("tickets/follow-up"), EnableRateLimiting("auth"), RequestSizeLimit(64 * 1024)]
    public Task<TicketDto> FollowUp(FollowUpBody b, CancellationToken ct) => mediator.Send(new FollowUpTicketCommand(b.Number, b.Phone, b.Body), ct);
}
