using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Exemptions;

namespace Sammha.Api.Controllers;

public record ExemptionRequestBody(string CamelChip, string CamelType, Guid FestivalId, string? Race, string? Note);
public record ExemptionPriceBody(decimal Amount, string? Note);
public record ExemptionReasonBody(string Reason);

/// <summary>
/// «طلب استثناء المشاركة بدون اسم»: the owner asks, staff set the fine for not naming the camel (exemptions.manage), the owner pays it by card,
/// and a signed exemption document is issued that anyone can verify.
/// </summary>
[ApiController, Route("api")]
public class ExemptionsController(ISender mediator) : ControllerBase
{
    // ───── anyone ─────
    /// <summary>public verification of an exemption document; `s` is the code from its QR or print</summary>
    [HttpGet("exemptions/issued/{number}"), AllowAnonymous, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("check")]
    public Task<ExemptionVerifyDto?> Verify(string number, [FromQuery] string? s, CancellationToken ct) => mediator.Send(new VerifyExemptionQuery(number, s), ct);

    // ───── owner ─────
    [HttpGet("me/exemptions/festivals"), Authorize]
    public Task<List<FestivalOptionDto>> Festivals(CancellationToken ct) => mediator.Send(new ListExemptionFestivalsQuery(), ct);

    [HttpGet("me/exemptions"), Authorize]
    public Task<List<ExemptionDto>> Mine(CancellationToken ct) => mediator.Send(new GetMyExemptionsQuery(), ct);

    /// <summary>one request by its number (the owner's own, or any for staff with exemptions.view)</summary>
    [HttpGet("me/exemptions/{number}"), Authorize]
    public Task<ExemptionDto> One(string number, CancellationToken ct) => mediator.Send(new GetMyExemptionQuery(number), ct);

    [HttpPost("me/exemptions"), Authorize]
    public Task<ExemptionDto> Submit(ExemptionRequestBody b, CancellationToken ct) => mediator.Send(new RequestExemptionCommand(b.CamelChip, b.CamelType, b.FestivalId, b.Race, b.Note), ct);

    [HttpPost("me/exemptions/{id:guid}/cancel"), Authorize]
    public Task<ExemptionDto> Cancel(Guid id, CancellationToken ct) => mediator.Send(new CancelMyExemptionCommand(id), ct);

    /// <summary>after the card payment (purpose "exemption"); the wallet cannot pay fines</summary>
    [HttpPost("me/exemptions/{id:guid}/pay"), Authorize]
    public Task<ExemptionDto> Pay(Guid id, CancellationToken ct) => mediator.Send(new PayExemptionCommand(id), ct);

    // ───── staff ─────
    [HttpGet("admin/exemptions"), Authorize(Policy = "staff")]
    public Task<ExemptionPageDto> List([FromQuery] string? q, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int size = 50, [FromQuery] Guid? festivalId = null, CancellationToken ct = default) =>
        mediator.Send(new ListExemptionsQuery(q, status, page, size, festivalId), ct);

    [HttpGet("admin/exemptions/{id:guid}"), Authorize(Policy = "staff")]
    public Task<ExemptionDto> Get(Guid id, CancellationToken ct) => mediator.Send(new GetExemptionQuery(id), ct);

    [HttpPost("admin/exemptions/{id:guid}/price"), Authorize(Policy = "staff")]
    public Task<ExemptionDto> Price(Guid id, ExemptionPriceBody b, CancellationToken ct) => mediator.Send(new PriceExemptionCommand(id, b.Amount, b.Note), ct);

    [HttpPost("admin/exemptions/{id:guid}/reject"), Authorize(Policy = "staff")]
    public Task<ExemptionDto> Reject(Guid id, ExemptionReasonBody b, CancellationToken ct) => mediator.Send(new RejectExemptionCommand(id, b.Reason), ct);

    [HttpPost("admin/exemptions/{id:guid}/revoke"), Authorize(Policy = "staff")]
    public Task<ExemptionDto> Revoke(Guid id, ExemptionReasonBody b, CancellationToken ct) => mediator.Send(new RevokeExemptionCommand(id, b.Reason), ct);
}
