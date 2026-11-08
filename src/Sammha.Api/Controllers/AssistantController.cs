using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Features.Assistant;

namespace Sammha.Api.Controllers;

public record AssistantBody(string Op, string? Phone, Guid? StaffId, string? Q, string? State);

/// <summary>
/// The staff WhatsApp assistant (n8n) reads the platform here, read-only. Not a user session: the caller proves itself with
/// the secret in the X-Assistant-Key header, and each answer is limited to the permissions of the staff member it is asked for.
/// </summary>
[ApiController, Route("api/assistant")]
public class AssistantController(ISender mediator, IOptions<AssistantOptions> opt) : ControllerBase
{
    [HttpPost("ask"), EnableRateLimiting("auth"), RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Ask(AssistantBody b, CancellationToken ct)
    {
        var o = opt.Value;
        if (!o.Enabled) return NotFound();
        var given = Request.Headers["X-Assistant-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(o.ApiKey!))))
            return Unauthorized();
        return Ok(await mediator.Send(new AssistantAskQuery(b.Op, b.Phone, b.StaffId, b.Q, b.State), ct));
    }
}
