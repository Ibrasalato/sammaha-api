using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Bans;

namespace Sammha.Api.Controllers;

public record BanBody(Guid? OwnerId, string? Owner, string? NationalId, string? Name, string Reason, DateOnly StartsOn, DateOnly? EndsOn);
public record BanUpdateBody(string Reason, DateOnly StartsOn, DateOnly? EndsOn);
public record BanLiftBody(string Reason);

/// <summary>«الحظر»: owners barred from bidding, buying, selling and new requests for a period (bans.view, bans.manage)</summary>
[ApiController, Route("api")]
public class BansController(ISender mediator) : ControllerBase
{
    [HttpGet("admin/bans"), Authorize(Policy = "staff")]
    public Task<BanPageDto> List([FromQuery] string? q, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default) =>
        mediator.Send(new ListBansQuery(q, status, page, size), ct);

    /// <summary>POST keeps the phone or ID number out of the address and the logs</summary>
    [HttpPost("admin/bans"), Authorize(Policy = "staff")]
    public Task<BanDto> Create(BanBody b, CancellationToken ct) => mediator.Send(new CreateBanCommand(b.OwnerId, b.Owner, b.NationalId, b.Name, b.Reason, b.StartsOn, b.EndsOn), ct);

    [HttpPut("admin/bans/{id:guid}"), Authorize(Policy = "staff")]
    public Task<BanDto> Update(Guid id, BanUpdateBody b, CancellationToken ct) => mediator.Send(new UpdateBanCommand(id, b.Reason, b.StartsOn, b.EndsOn), ct);

    [HttpPost("admin/bans/{id:guid}/lift"), Authorize(Policy = "staff")]
    public Task<BanDto> Lift(Guid id, BanLiftBody b, CancellationToken ct) => mediator.Send(new LiftBanCommand(id, b.Reason), ct);

    /// <summary>the ban in force on the signed-in owner (204 when none)</summary>
    [HttpGet("me/ban"), Authorize]
    public Task<MyBanDto?> Mine(CancellationToken ct) => mediator.Send(new GetMyBanQuery(), ct);
}
