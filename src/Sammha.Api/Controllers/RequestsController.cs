using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Common;
using Sammha.Application.Features.Requests;

namespace Sammha.Api.Controllers;

[ApiController, Route("api/requests"), Authorize]
public class RequestsController(ISender mediator) : ControllerBase
{
    /// <summary>the owner's own request, or any request for staff</summary>
    [HttpGet("{id:guid}")]
    public Task<RequestDto> Get(Guid id, CancellationToken ct) => mediator.Send(new GetRequestQuery(id), ct);
}
