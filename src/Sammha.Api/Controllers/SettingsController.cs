using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Settings;

namespace Sammha.Api.Controllers;

/// <summary>Public switches the site reads on start: open services, banner, contact details, password rule.</summary>
[ApiController, Route("api/settings")]
public class SettingsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public Task<PublicSettingsDto> Get(CancellationToken ct) => mediator.Send(new GetPublicSettingsQuery(), ct);
}

/// <summary>Platform settings (super admin and admins).</summary>
[ApiController, Route("api/admin/settings"), Authorize(Policy = "admin")]
public class AdminSettingsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public Task<AdminSettingsDto> Get(CancellationToken ct) => mediator.Send(new GetAdminSettingsQuery(), ct);

    /// <summary>Partial update: send only the fields to change. Every change is written to the history.</summary>
    [HttpPatch]
    public Task<AdminSettingsDto> Update(SettingsPatch body, CancellationToken ct) => mediator.Send(new UpdateSettingsCommand(body), ct);

    [HttpGet("history")]
    public Task<List<SettingsChangeDto>> History([FromQuery] int take = 100, CancellationToken ct = default) => mediator.Send(new GetSettingsHistoryQuery(take), ct);

    /// <summary>Emergency: ends every session on the platform except yours.</summary>
    [HttpPost("sign-out-everyone")]
    public async Task<object> SignOutEveryone(CancellationToken ct) => new { users = await mediator.Send(new SignOutEveryoneCommand(), ct) };
}

/// <summary>Personal settings for any signed-in user (owners and staff).</summary>
[ApiController, Route("api/me"), Authorize]
public class MySettingsController(ISender mediator) : ControllerBase
{
    [HttpGet("preferences")]
    public Task<PreferencesDto> Preferences(CancellationToken ct) => mediator.Send(new GetMyPreferencesQuery(), ct);

    [HttpPut("preferences")]
    public Task<PreferencesDto> SavePreferences(PreferencesDto body, CancellationToken ct) => mediator.Send(new UpdateMyPreferencesCommand(body), ct);

    /// <summary>Devices signed in to this account; Current marks this one.</summary>
    [HttpGet("sessions")]
    public Task<List<SessionDto>> Sessions(CancellationToken ct) => mediator.Send(new GetMySessionsQuery(), ct);

    [HttpDelete("sessions/{id:guid}")]
    public Task<List<SessionDto>> Revoke(Guid id, CancellationToken ct) => mediator.Send(new RevokeMySessionCommand(id), ct);

    [HttpGet("security")]
    public Task<SecuritySummaryDto> Security(CancellationToken ct) => mediator.Send(new GetMySecurityQuery(), ct);
}
