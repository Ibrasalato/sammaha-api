using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sammha.Application.Common;
using Sammha.Application.Features;

namespace Sammha.Api.Controllers;

/// <summary>Pages anyone can open.</summary>
[ApiController, Route("api")]
public class PublicController(CatalogService catalog, ListingService listings, TicketService tickets) : ControllerBase
{
    [HttpGet("stats")] public Task<PublicStatsDto> Stats(CancellationToken ct) => catalog.StatsAsync(ct);

    [HttpGet("auctions")]
    public Task<List<AuctionDto>> Auctions([FromQuery] string? phase, [FromQuery] string? type, [FromQuery] string? q, CancellationToken ct) =>
        catalog.ListAuctionsAsync(new AuctionFilter(phase, type, q), ct);

    [HttpGet("auctions/{id:guid}")] public Task<AuctionViewDto> Auction(Guid id, CancellationToken ct) => catalog.GetAuctionAsync(id, ct);

    [HttpGet("names")]
    public Task<List<ReservedNameDto>> Names([FromQuery] string? q, [FromQuery] string? type, CancellationToken ct) => catalog.ListReservedAsync(q, type, ct);

    [HttpPost("names/check"), EnableRateLimiting("check")]
    public Task<List<NameCheckDto>> Check(CheckBody b, CancellationToken ct) => catalog.CheckNamesAsync(b.Names, ct);

    [HttpGet("events")] public Task<List<CamelEventDto>> Events(CancellationToken ct) => catalog.EventsAsync(ct);

    [HttpGet("certificates/{code}")] public Task<ReservedNameDto?> Verify(string code, CancellationToken ct) => catalog.VerifyAsync(code, ct);

    [HttpGet("market")] public Task<List<ListingDto>> Market(CancellationToken ct) => listings.MarketAsync(ct);

    [HttpPost("market/{id:guid}/buy"), Authorize]
    public Task<ListingDto> Buy(Guid id, CancellationToken ct) => listings.BuyAsync(id, ct);

    [HttpPost("tickets"), EnableRateLimiting("auth")]
    public Task<TicketDto> Submit(TicketInput b, CancellationToken ct) => tickets.SubmitAsync(b, ct);

    [HttpPost("tickets/track"), EnableRateLimiting("auth")]
    public Task<TicketDto?> Track(TrackBody b, CancellationToken ct) => tickets.TrackAsync(b.Number, b.Phone, ct);

    [HttpPost("tickets/follow-up"), EnableRateLimiting("auth")]
    public Task<TicketDto> FollowUp(FollowUpBody b, CancellationToken ct) => tickets.FollowUpAsync(b.Number, b.Phone, b.Body, ct);
}
