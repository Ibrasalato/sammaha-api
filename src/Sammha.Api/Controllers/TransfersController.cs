using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sammha.Application.Features.Transfers;

namespace Sammha.Api.Controllers;

public record NameTransferBody(Guid ReservedId, string Phone, string Reason, string? Note);
public record RecipientBody(string Phone);
public record RespondBody(bool Accept);
public record TransferReviewBody(bool Approve, string? Note);

/// <summary>«نقل ملكية اسم»: owner → recipient accepts → committee approves</summary>
[ApiController, Route("api"), Authorize]
public class TransfersController(ISender mediator) : ControllerBase
{
    [HttpGet("me/transfers")]
    public Task<MyTransfersDto> Mine(CancellationToken ct) => mediator.Send(new GetMyTransfersQuery(), ct);

    /// <summary>check the recipient's mobile before sending: returns a masked name only</summary>
    [HttpPost("me/transfers/recipient"), EnableRateLimiting("check")]
    public Task<RecipientDto> Recipient(RecipientBody b, CancellationToken ct) => mediator.Send(new FindRecipientQuery(b.Phone), ct);

    [HttpPost("me/transfers")]
    public Task<TransferDto> Create(NameTransferBody b, CancellationToken ct) => mediator.Send(new CreateTransferCommand(b.ReservedId, b.Phone, b.Reason, b.Note), ct);

    [HttpPost("me/transfers/{id:guid}/cancel")]
    public Task<TransferDto> Cancel(Guid id, CancellationToken ct) => mediator.Send(new CancelTransferCommand(id), ct);

    [HttpPost("me/transfers/{id:guid}/respond")]
    public Task<TransferDto> Respond(Guid id, RespondBody b, CancellationToken ct) => mediator.Send(new RespondTransferCommand(id, b.Accept), ct);

    [HttpGet("admin/transfers")]
    public Task<List<TransferDto>> List([FromQuery] string? status, CancellationToken ct) => mediator.Send(new ListTransfersQuery(status), ct);

    [HttpPost("admin/transfers/{id:guid}/review")]
    public Task<TransferDto> Review(Guid id, TransferReviewBody b, CancellationToken ct) => mediator.Send(new ReviewTransferCommand(id, b.Approve, b.Note), ct);
}
