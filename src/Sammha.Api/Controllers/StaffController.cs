using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features.Auctions;
using Sammha.Application.Features.Staff;

namespace Sammha.Api.Controllers;

public record StaffRoleBody(Guid StaffRoleId);
public record PauseBody(AuctionScope Scope, string? Reason);
public record ScopeBody(AuctionScope Scope);
public record ExtendBody(AuctionScope Scope, int Minutes);
public record CancelBody(AuctionScope Scope, string Reason);

/// <summary>Staff roles and permissions, and who handles which kind of ticket (each handler checks its permission).</summary>
[ApiController, Route("api/admin"), Authorize(Policy = "staff")]
public class StaffController(ISender mediator) : ControllerBase
{
    /// <summary>Every platform function that can be granted to a role.</summary>
    [HttpGet("permissions")]
    public Task<List<PermissionDto>> Permissions(CancellationToken ct) => mediator.Send(new GetPermissionCatalogQuery(), ct);

    [HttpGet("roles")]
    public Task<List<StaffRoleDto>> Roles(CancellationToken ct) => mediator.Send(new ListStaffRolesQuery(), ct);

    [HttpPost("roles")]
    public Task<StaffRoleDto> Create(StaffRoleInput body, CancellationToken ct) => mediator.Send(new SaveStaffRoleCommand(null, body), ct);

    [HttpPut("roles/{id:guid}")]
    public Task<StaffRoleDto> Update(Guid id, StaffRoleInput body, CancellationToken ct) => mediator.Send(new SaveStaffRoleCommand(id, body), ct);

    [HttpDelete("roles/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await mediator.Send(new DeleteStaffRoleCommand(id), ct); return NoContent(); }

    /// <summary>Give a staff member a role (its permissions apply at once).</summary>
    [HttpPut("users/{id:guid}/staff-role")]
    public Task<UserDto> SetStaffRole(Guid id, StaffRoleBody body, CancellationToken ct) => mediator.Send(new SetUserStaffRoleCommand(id, body.StaffRoleId), ct);

    /// <summary>Who handles complaints, reports, enquiries and suggestions, their team inboxes, and whether only they may reply.</summary>
    [HttpGet("ticket-routing")]
    public Task<TicketRoutingDto> Routing(CancellationToken ct) => mediator.Send(new GetTicketRoutingQuery(), ct);

    [HttpPut("ticket-routing")]
    public Task<TicketRoutingDto> SaveRouting(TicketRoutingInput body, CancellationToken ct) => mediator.Send(new SaveTicketRoutingCommand(body), ct);
}

/// <summary>Auction controls: pause, resume, extend or cancel one auction, a selection, or all of them (optionally one type).</summary>
[ApiController, Route("api/admin/auctions"), Authorize(Policy = "staff")]
public class AuctionControlController(ISender mediator) : ControllerBase
{
    /// <summary>state: active | live | scheduled | paused | cancelled | ended | all</summary>
    [HttpGet]
    public Task<List<AuctionDto>> List([FromQuery] string? state, [FromQuery] string? type, CancellationToken ct) => mediator.Send(new ListAdminAuctionsQuery(state, type), ct);

    [HttpPost("pause")]
    public Task<AuctionActionResult> Pause(PauseBody b, CancellationToken ct) => mediator.Send(new PauseAuctionsCommand(b.Scope, b.Reason), ct);

    [HttpPost("resume")]
    public Task<AuctionActionResult> Resume(ScopeBody b, CancellationToken ct) => mediator.Send(new ResumeAuctionsCommand(b.Scope), ct);

    [HttpPost("extend")]
    public Task<AuctionActionResult> Extend(ExtendBody b, CancellationToken ct) => mediator.Send(new ExtendAuctionsCommand(b.Scope, b.Minutes), ct);

    [HttpPost("cancel")]
    public Task<AuctionActionResult> Cancel(CancelBody b, CancellationToken ct) => mediator.Send(new CancelAuctionsCommand(b.Scope, b.Reason), ct);
}
