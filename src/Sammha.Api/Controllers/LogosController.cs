using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features.Logos;

namespace Sammha.Api.Controllers;

public record LogoBody(string FileName, string Base64);
public record LogoReviewBody(bool Approve, string? Reason);

/// <summary>logos of owned marks: the owner uploads, the committee approves, everyone sees approved ones</summary>
[ApiController, Route("api")]
public class LogosController(ISender mediator) : ControllerBase
{
    private IActionResult Image(LogoFile? f, bool cache)
    {
        if (f == null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = cache ? "public, max-age=86400" : "private, no-store";
        return File(f.Bytes, f.ContentType);
    }

    [HttpGet("names/{id:guid}/logo"), AllowAnonymous]
    public async Task<IActionResult> Public(Guid id, CancellationToken ct) => Image(await mediator.Send(new GetLogoQuery(id), ct), true);

    [HttpGet("me/names/{id:guid}/logo"), Authorize]
    public async Task<IActionResult> Mine(Guid id, CancellationToken ct) => Image(await mediator.Send(new GetMyLogoQuery(id), ct), false);

    [HttpPut("me/names/{id:guid}/logo"), Authorize, RequestSizeLimit(4 * 1024 * 1024)]
    public Task<ReservedNameDto> Upload(Guid id, LogoBody b, CancellationToken ct) => mediator.Send(new UploadLogoCommand(id, b.FileName, b.Base64), ct);

    [HttpDelete("me/names/{id:guid}/logo"), Authorize]
    public Task<ReservedNameDto> Delete(Guid id, CancellationToken ct) => mediator.Send(new DeleteLogoCommand(id), ct);

    [HttpGet("admin/logos"), Authorize(Policy = "staff")]
    public Task<List<LogoDto>> List([FromQuery] string? status, CancellationToken ct) => mediator.Send(new ListLogosQuery(status), ct);

    [HttpGet("admin/logos/{id:guid}/image"), Authorize(Policy = "staff")]
    public async Task<IActionResult> StaffImage(Guid id, CancellationToken ct) => Image(await mediator.Send(new GetStaffLogoQuery(id), ct), false);

    [HttpPost("admin/logos/{id:guid}/review"), Authorize(Policy = "staff")]
    public Task<LogoDto> Review(Guid id, LogoReviewBody b, CancellationToken ct) => mediator.Send(new ReviewLogoCommand(id, b.Approve, b.Reason), ct);
}
