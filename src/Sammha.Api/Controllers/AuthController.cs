using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sammha.Application.Common;
using Sammha.Application.Features.Auth;

namespace Sammha.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(ISender mediator) : ControllerBase
{
    [HttpGet("me")]
    public Task<UserDto?> Me(CancellationToken ct) => mediator.Send(new GetCurrentUserQuery(), ct);

    [HttpPost("signup"), EnableRateLimiting("auth")]
    public Task<AuthResult> SignUp(SignUpInput body, CancellationToken ct) => mediator.Send(new SignUpCommand(body), ct);

    /// <summary>step 1: password → one-time code challenge</summary>
    [HttpPost("signin"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> SignIn(SignInBody b, CancellationToken ct) => mediator.Send(new StartSignInCommand(b.Login, b.Password, b.Portal), ct);

    /// <summary>step 2: the code → token</summary>
    [HttpPost("signin/complete"), EnableRateLimiting("auth")]
    public Task<AuthResult> Complete(CodeBody b, CancellationToken ct) => mediator.Send(new CompleteSignInCommand(b.Challenge, b.Code), ct);

    [HttpPost("otp/resend"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> Resend(ChallengeBody b, CancellationToken ct) => mediator.Send(new ResendOtpCommand(b.Challenge), ct);

    /// <summary>signs every other device out and returns a new session for this one</summary>
    [HttpPost("password"), Authorize]
    public Task<AuthResult> Password(PasswordBody b, CancellationToken ct) => mediator.Send(new ChangePasswordCommand(b.Current, b.Next), ct);

    /// <summary>refresh token → new access token + new refresh token (the old one stops working)</summary>
    [HttpPost("refresh"), EnableRateLimiting("refresh")]
    public Task<AuthResult> Refresh(RefreshBody b, CancellationToken ct) => mediator.Send(new RefreshSessionCommand(b.RefreshToken), ct);

    /// <summary>revokes this device's refresh token (or every session with everywhere=true)</summary>
    [HttpPost("signout")]
    public async Task<IActionResult> SignOutUser(SignOutBody b, CancellationToken ct)
    {
        await mediator.Send(new SignOutCommand(b.RefreshToken, b.Everywhere), ct);
        return NoContent();
    }
}
