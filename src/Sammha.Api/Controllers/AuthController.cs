using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sammha.Application.Common;
using Sammha.Application.Features;

namespace Sammha.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpGet("me")] public async Task<UserDto?> Me(CancellationToken ct) => await auth.CurrentAsync(ct);

    [HttpPost("signup"), EnableRateLimiting("auth")]
    public Task<AuthResult> SignUp(SignUpInput body, CancellationToken ct) => auth.SignUpAsync(body, ct);

    /// <summary>step 1: password → one-time code challenge</summary>
    [HttpPost("signin"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> SignIn(SignInBody b, CancellationToken ct) => auth.StartSignInAsync(b.Login, b.Password, b.Portal, ct);

    /// <summary>step 2: the code → token</summary>
    [HttpPost("signin/complete"), EnableRateLimiting("auth")]
    public Task<AuthResult> Complete(CodeBody b, CancellationToken ct) => auth.CompleteSignInAsync(b.Challenge, b.Code, ct);

    [HttpPost("otp/resend"), EnableRateLimiting("auth")]
    public Task<OtpChallengeDto> Resend(ChallengeBody b, CancellationToken ct) => auth.ResendOtpAsync(b.Challenge, ct);

    [HttpPost("password"), Authorize]
    public async Task<IActionResult> Password(PasswordBody b, CancellationToken ct) { await auth.ChangePasswordAsync(b.Current, b.Next, ct); return NoContent(); }

    /// <summary>tokens are stateless; the client forgets it</summary>
    [HttpPost("signout")] public IActionResult SignOutUser() => NoContent();
}
