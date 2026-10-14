using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Payments;

namespace Sammha.Api.Controllers;

[ApiController, Route("api/payments")]
public class PaymentsController(ISender mediator) : ControllerBase
{
    /// <summary>starts a card payment; returns the Moyasar page to redirect to (url is null when simulated)</summary>
    [HttpPost("card"), Authorize]
    public Task<CardCheckout> Card(CardBody b, CancellationToken ct) => mediator.Send(new StartCardPaymentCommand(b.Purpose, b.Ref, b.Amount, b.Callback, b.Method), ct);

    /// <summary>Moyasar webhook (configure secret_token = Payments:WebhookSecret)</summary>
    [HttpPost("moyasar/webhook"), AllowAnonymous]
    public async Task<IActionResult> Webhook([FromBody] JsonElement body, CancellationToken ct)
    {
        await mediator.Send(new MoyasarWebhookCommand(body), ct);
        return Ok(new { ok = true });
    }
}
