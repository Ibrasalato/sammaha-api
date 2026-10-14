using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Finance;
using Sammha.Application.Features.Violations;

namespace Sammha.Api.Controllers;

public record ViolationTypeBody(string Name, string? NameEn, decimal Amount, bool Active = true, bool AllowsNameResolution = false);
public record FestivalBody(string Name, string? NameEn, int? Year, DateOnly? StartsOn, DateOnly? EndsOn, bool Active = true);
public record ViolationBody(Guid? OwnerId, string? Owner, Guid TypeId, string Description, ViolationAttachmentInput? Attachment, Guid? FestivalId = null, string? CamelChip = null);
public record ViolationNoteBody(string? Note);
public record ViolationCancelBody(string Reason);
public record OwnerLookupBody(string Q);

/// <summary>«المخالفات»: types in the settings (settings.manage), issuing and cancelling (violations.manage), the list (violations.view), and the owner's own violations</summary>
[ApiController, Route("api")]
public class ViolationsController(ISender mediator) : ControllerBase
{
    // ───── settings: violation types ─────
    [HttpGet("admin/violation-types"), Authorize(Policy = "staff")]
    public Task<List<ViolationTypeDto>> Types([FromQuery] bool active = false, CancellationToken ct = default) => mediator.Send(new ListViolationTypesQuery(active), ct);

    [HttpPost("admin/violation-types"), Authorize(Policy = "staff")]
    public Task<ViolationTypeDto> CreateType(ViolationTypeBody b, CancellationToken ct) => mediator.Send(new SaveViolationTypeCommand(null, b.Name, b.NameEn, b.Amount, b.Active, b.AllowsNameResolution), ct);

    [HttpPut("admin/violation-types/{id:guid}"), Authorize(Policy = "staff")]
    public Task<ViolationTypeDto> UpdateType(Guid id, ViolationTypeBody b, CancellationToken ct) => mediator.Send(new SaveViolationTypeCommand(id, b.Name, b.NameEn, b.Amount, b.Active, b.AllowsNameResolution), ct);

    [HttpDelete("admin/violation-types/{id:guid}"), Authorize(Policy = "staff")]
    public async Task<IActionResult> DeleteType(Guid id, CancellationToken ct) { await mediator.Send(new DeleteViolationTypeCommand(id), ct); return NoContent(); }

    // ───── settings: festivals ─────
    [HttpGet("admin/festivals"), Authorize(Policy = "staff")]
    public Task<List<FestivalDto>> Festivals([FromQuery] bool active = false, CancellationToken ct = default) => mediator.Send(new ListFestivalsQuery(active), ct);

    [HttpPost("admin/festivals"), Authorize(Policy = "staff")]
    public Task<FestivalDto> CreateFestival(FestivalBody b, CancellationToken ct) => mediator.Send(new SaveFestivalCommand(null, b.Name, b.NameEn, b.Year, b.StartsOn, b.EndsOn, b.Active), ct);

    [HttpPut("admin/festivals/{id:guid}"), Authorize(Policy = "staff")]
    public Task<FestivalDto> UpdateFestival(Guid id, FestivalBody b, CancellationToken ct) => mediator.Send(new SaveFestivalCommand(id, b.Name, b.NameEn, b.Year, b.StartsOn, b.EndsOn, b.Active), ct);

    [HttpDelete("admin/festivals/{id:guid}"), Authorize(Policy = "staff")]
    public async Task<IActionResult> DeleteFestival(Guid id, CancellationToken ct) { await mediator.Send(new DeleteFestivalCommand(id), ct); return NoContent(); }

    // ───── staff ─────
    [HttpGet("admin/violations"), Authorize(Policy = "staff")]
    public Task<ViolationPageDto> List([FromQuery] string? q, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int size = 50, [FromQuery] Guid? festivalId = null, CancellationToken ct = default) =>
        mediator.Send(new ListViolationsQuery(q, status, page, size, festivalId), ct);

    /// <summary>find the owner to issue to by mobile or identity number (POST keeps the number out of the address and the logs)</summary>
    [HttpPost("admin/violations/owner"), Authorize(Policy = "staff")]
    public Task<ViolationOwnerDto> FindOwner(OwnerLookupBody b, CancellationToken ct) => mediator.Send(new FindViolationOwnerQuery(b.Q), ct);

    [HttpGet("admin/violations/{id:guid}"), Authorize(Policy = "staff")]
    public Task<ViolationDto> Get(Guid id, CancellationToken ct) => mediator.Send(new GetViolationQuery(id), ct);

    [HttpGet("admin/violations/{id:guid}/attachment"), Authorize(Policy = "staff")]
    public Task<DocumentDto> Attachment(Guid id, CancellationToken ct) => mediator.Send(new GetViolationAttachmentQuery(id), ct);

    [HttpPost("admin/violations"), Authorize(Policy = "staff"), RequestSizeLimit(8 * 1024 * 1024)]
    public Task<ViolationDto> Issue(ViolationBody b, CancellationToken ct) => mediator.Send(new IssueViolationCommand(b.OwnerId, b.Owner, b.TypeId, b.Description, b.Attachment, b.FestivalId, b.CamelChip), ct);

    [HttpPost("admin/violations/{id:guid}/cancel"), Authorize(Policy = "staff")]
    public Task<ViolationDto> Cancel(Guid id, ViolationCancelBody b, CancellationToken ct) => mediator.Send(new CancelViolationCommand(id, b.Reason), ct);

    /// <summary>«تسوية بتسجيل اسم»: the owner registered a name for the camel; closes an unpaid violation with no payment</summary>
    [HttpPost("admin/violations/{id:guid}/resolve-name"), Authorize(Policy = "staff")]
    public Task<ViolationDto> ResolveByName(Guid id, ViolationNoteBody b, CancellationToken ct) => mediator.Send(new ResolveViolationByNameCommand(id, b.Note), ct);

    /// <summary>«منع من المشاركة»: the camel is barred from the festival</summary>
    [HttpPost("admin/violations/{id:guid}/ban"), Authorize(Policy = "staff")]
    public Task<ViolationDto> Ban(Guid id, ViolationCancelBody b, CancellationToken ct) => mediator.Send(new BanViolationCommand(id, b.Reason), ct);

    // ───── owner ─────
    [HttpGet("me/violations"), Authorize]
    public Task<List<ViolationDto>> Mine(CancellationToken ct) => mediator.Send(new GetMyViolationsQuery(), ct);

    [HttpGet("me/violations/{id:guid}/attachment"), Authorize]
    public Task<DocumentDto> MyAttachment(Guid id, CancellationToken ct) => mediator.Send(new GetMyViolationAttachmentQuery(id), ct);

    /// <summary>after the card payment (purpose "violation"); the wallet cannot pay fines</summary>
    [HttpPost("me/violations/{id:guid}/pay"), Authorize]
    public Task<ViolationDto> Pay(Guid id, CancellationToken ct) => mediator.Send(new PayViolationCommand(id), ct);
}
