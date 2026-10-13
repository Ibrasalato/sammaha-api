using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features;

namespace Sammha.Api.Controllers;

/// <summary>Approval committee and administration (the service layer re-checks every permission).</summary>
[ApiController, Route("api/admin"), Authorize(Policy = "staff")]
public class AdminController(AdminService admin, TicketService tickets) : ControllerBase
{
    [HttpGet("overview")] public Task<AdminOverviewDto> Overview(CancellationToken ct) => admin.OverviewAsync(ct);

    [HttpGet("requests")] public Task<List<RequestDto>> Requests([FromQuery] string? status, CancellationToken ct) => admin.ListRequestsAsync(status, ct);
    [HttpPost("requests/{id:guid}/review")] public Task<RequestDto> Review(Guid id, ReviewBody b, CancellationToken ct) => admin.ReviewAsync(id, b.Decisions, b.Note, ct);
    [HttpPost("requests/{id:guid}/reject")] public Task<RequestDto> Reject(Guid id, ReasonBody b, CancellationToken ct) => admin.RejectAsync(id, b.Reason ?? "", ct);
    [HttpPost("requests/{id:guid}/cancel")] public Task<RequestDto> Cancel(Guid id, ReasonBody b, CancellationToken ct) => admin.CancelAsync(id, b.Reason ?? "", ct);

    [HttpGet("schedule")] public Task<List<AwaitingDto>> Awaiting(CancellationToken ct) => admin.AwaitingScheduleAsync(ct);
    [HttpPost("schedule")] public Task<List<AuctionDto>> Schedule(ScheduleBody b, CancellationToken ct) => admin.ScheduleAsync(b.CandidateIds, b.StartsAt, b.EndsAt, ct);

    [HttpGet("invoices")] public Task<List<InvoiceDto>> Invoices(CancellationToken ct) => admin.AllInvoicesAsync(ct);
    [HttpPost("invoices/{id:guid}/paid")] public Task<InvoiceDto> MarkPaid(Guid id, CancellationToken ct) => admin.MarkInvoicePaidAsync(id, ct);

    [HttpGet("users")] public Task<List<UserDto>> Users(CancellationToken ct) => admin.ListUsersAsync(ct);
    [HttpPost("users"), Authorize(Policy = "admin")] public Task<UserDto> CreateStaff(StaffInput b, CancellationToken ct) => admin.CreateStaffAsync(b, ct);
    [HttpPut("users/{id:guid}/role"), Authorize(Policy = "admin")] public Task<UserDto> Role(Guid id, RoleBody b, CancellationToken ct) => admin.SetRoleAsync(id, b.Role, ct);
    [HttpPut("users/{id:guid}/active"), Authorize(Policy = "admin")] public Task<UserDto> Active(Guid id, ActiveBody b, CancellationToken ct) => admin.SetActiveAsync(id, b.Active, ct);

    [HttpPut("events")] public Task<CamelEventDto> SaveEvent(CamelEventDto b, CancellationToken ct) => admin.SaveEventAsync(b, ct);
    [HttpDelete("events/{id}")] public async Task<IActionResult> DeleteEvent(string id, CancellationToken ct) { await admin.DeleteEventAsync(id, ct); return NoContent(); }

    [HttpGet("tickets")] public Task<List<TicketDto>> Tickets(CancellationToken ct) => tickets.ListAsync(ct);
    [HttpGet("tickets/{id:guid}")] public Task<TicketDto> Ticket(Guid id, CancellationToken ct) => tickets.GetAsync(id, ct);
    [HttpPatch("tickets/{id:guid}")] public Task<TicketDto> Update(Guid id, TicketPatch b, CancellationToken ct) => tickets.UpdateAsync(id, b.Status, b.Priority, b.Assignee, ct);
    [HttpPost("tickets/{id:guid}/reply")] public Task<TicketDto> Reply(Guid id, ReplyBody b, CancellationToken ct) => tickets.ReplyAsync(id, b.Body, b.Internal, ct);
}

[ApiController, Route("api/payments")]
public class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>starts a card payment; returns the Moyasar page to redirect to (url is null when simulated)</summary>
    [HttpPost("card"), Authorize]
    public Task<CardCheckout> Card(CardBody b, CancellationToken ct) => payments.StartAsync(b.Purpose, b.Ref, b.Amount, b.Callback, ct);

    /// <summary>Moyasar webhook (configure secret_token = Payments:WebhookSecret)</summary>
    [HttpPost("moyasar/webhook"), AllowAnonymous]
    public async Task<IActionResult> Webhook([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
    {
        await payments.HandleWebhookAsync(body, ct);
        return Ok(new { ok = true });
    }
}
