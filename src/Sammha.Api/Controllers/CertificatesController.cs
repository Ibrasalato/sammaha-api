using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features.Certificates;

namespace Sammha.Api.Controllers;

/// <summary>Bilingual registration certificates: owners request them, anyone verifies them, staff review and revoke.</summary>
[ApiController, Route("api")]
public class CertificatesController(ISender mediator) : ControllerBase
{
    public record IssueBody(string Scope, Guid? ReservedNameId, string? OwnerNameEn);
    public record RevokeBody(string Reason);
    public record LatinBody(string? Latin);

    /// <summary>public verification of an issued certificate; `s` is the signature code from its QR or print</summary>
    [HttpGet("certificates/issued/{number}"), AllowAnonymous, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("check")]
    public Task<CertificateVerifyDto?> Verify(string number, [FromQuery] string? s, CancellationToken ct) => mediator.Send(new VerifyIssuedCertificateQuery(number, s), ct);

    [HttpGet("me/certificates"), Authorize]
    public Task<List<CertificateDto>> Mine(CancellationToken ct) => mediator.Send(new ListMyCertificatesQuery(), ct);

    [HttpPost("me/certificates"), Authorize]
    public Task<CertificateDto> Issue(IssueBody b, CancellationToken ct) => mediator.Send(new IssueCertificateCommand(b.Scope, b.ReservedNameId, b.OwnerNameEn), ct);

    [HttpGet("me/certificates/{number}"), Authorize]
    public Task<CertificateDto> Get(string number, CancellationToken ct) => mediator.Send(new GetCertificateQuery(number), ct);

    [HttpGet("admin/certificates"), Authorize(Policy = "staff")]
    public Task<CertificatePage> List([FromQuery] string? q, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default) =>
        mediator.Send(new ListCertificatesQuery(q, status, page, size), ct);

    [HttpGet("admin/certificates/{number}"), Authorize(Policy = "staff")]
    public Task<CertificateDto> StaffGet(string number, CancellationToken ct) => mediator.Send(new GetCertificateQuery(number), ct);

    [HttpPost("admin/certificates/{id:guid}/revoke"), Authorize(Policy = "staff")]
    public Task<CertificateDto> Revoke(Guid id, RevokeBody b, CancellationToken ct) => mediator.Send(new RevokeCertificateCommand(id, b.Reason), ct);

    [HttpPut("admin/names/{id:guid}/latin"), Authorize(Policy = "staff")]
    public Task<ReservedNameDto> Latin(Guid id, LatinBody b, CancellationToken ct) => mediator.Send(new SetLatinNameCommand(id, b.Latin), ct);
}
