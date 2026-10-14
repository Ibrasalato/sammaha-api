using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features.Admin;
using Sammha.Application.Features.Tickets;

namespace Sammha.Api.Controllers;

/// <summary>Approval committee and administration (the handlers re-check every permission).</summary>
[ApiController, Route("api/admin"), Authorize(Policy = "staff")]
public class AdminController(ISender mediator) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AdminOverviewDto> Overview(CancellationToken ct) => mediator.Send(new GetOverviewQuery(), ct);

    // ───── review ─────
    [HttpGet("requests")]
    public Task<List<RequestDto>> Requests([FromQuery] string? status, CancellationToken ct) => mediator.Send(new ListRequestsQuery(status), ct);

    /// <summary>audit of requested names: matches with or without «ال», singular or plural, and the highest price of a similar name</summary>
    [HttpGet("requests/audit")]
    public Task<List<AuditRowDto>> Audit([FromQuery] string? status, [FromQuery] string? type, CancellationToken ct) => mediator.Send(new RequestsAuditQuery(status, type), ct);

    [HttpPost("requests/{id:guid}/review")]
    public Task<RequestDto> Review(Guid id, ReviewBody b, CancellationToken ct) => mediator.Send(new ReviewRequestCommand(id, b.Decisions, b.Note), ct);

    [HttpPost("requests/{id:guid}/reject")]
    public Task<RequestDto> Reject(Guid id, ReasonBody b, CancellationToken ct) => mediator.Send(new RejectRequestCommand(id, b.Reason ?? ""), ct);

    [HttpPost("requests/{id:guid}/cancel")]
    public Task<RequestDto> Cancel(Guid id, ReasonBody b, CancellationToken ct) => mediator.Send(new CancelRequestCommand(id, b.Reason ?? ""), ct);

    // ───── auctions and invoices ─────
    [HttpGet("schedule")]
    public Task<List<AwaitingDto>> Awaiting(CancellationToken ct) => mediator.Send(new GetAwaitingScheduleQuery(), ct);

    /// <summary>«استثناء اسم من المزاد»: register an accepted name to its requester for a fee, without an auction (requests.exception)</summary>
    [HttpPost("candidates/{id:guid}/exception")]
    public Task<RequestDto> Exception(Guid id, ExceptionBody b, CancellationToken ct) => mediator.Send(new Sammha.Application.Features.Admin.GrantExceptionCommand(id, b.Fee, b.Reason), ct);

    [HttpPost("schedule")]
    public Task<List<AuctionDto>> Schedule(ScheduleBody b, CancellationToken ct) => mediator.Send(new ScheduleAuctionsCommand(b.CandidateIds, b.StartsAt, b.EndsAt, b.StaggerMinutes), ct);

    [HttpGet("invoices")]
    public Task<List<InvoiceDto>> Invoices(CancellationToken ct) => mediator.Send(new ListInvoicesQuery(), ct);

    [HttpPost("invoices/{id:guid}/paid")]
    public Task<InvoiceDto> MarkPaid(Guid id, CancellationToken ct) => mediator.Send(new MarkInvoicePaidCommand(id), ct);

    // ───── users and permissions (super admin) ─────
    [HttpGet("users")]
    public Task<List<UserDto>> Users(CancellationToken ct) => mediator.Send(new ListUsersQuery(), ct);

    [HttpPost("users")]
    public Task<UserDto> CreateStaff(StaffInput b, CancellationToken ct) => mediator.Send(new CreateStaffCommand(b), ct);

    [HttpPut("users/{id:guid}/role")]
    public Task<UserDto> Role(Guid id, RoleBody b, CancellationToken ct) => mediator.Send(new SetUserRoleCommand(id, b.Role), ct);

    [HttpPut("users/{id:guid}/active")]
    public Task<UserDto> Active(Guid id, ActiveBody b, CancellationToken ct) => mediator.Send(new SetUserActiveCommand(id, b.Active), ct);

    // ───── events calendar ─────
    [HttpPut("events")]
    public Task<CamelEventDto> SaveEvent(CamelEventDto b, CancellationToken ct) => mediator.Send(new SaveEventCommand(b), ct);

    [HttpDelete("events/{id}")]
    public async Task<IActionResult> DeleteEvent(string id, CancellationToken ct) { await mediator.Send(new DeleteEventCommand(id), ct); return NoContent(); }

    // ───── complaints and reports ─────
    [HttpGet("tickets")]
    public Task<List<TicketDto>> Tickets(CancellationToken ct) => mediator.Send(new ListTicketsQuery(), ct);

    [HttpGet("tickets/{id:guid}")]
    public Task<TicketDto> Ticket(Guid id, CancellationToken ct) => mediator.Send(new GetTicketQuery(id), ct);

    [HttpPatch("tickets/{id:guid}")]
    public Task<TicketDto> Update(Guid id, TicketPatch b, CancellationToken ct) => mediator.Send(new UpdateTicketCommand(id, b.Status, b.Priority, b.Assignee), ct);

    [HttpPost("tickets/{id:guid}/reply")]
    public Task<TicketDto> Reply(Guid id, ReplyBody b, CancellationToken ct) => mediator.Send(new ReplyTicketCommand(id, b.Body, b.Internal), ct);
}
