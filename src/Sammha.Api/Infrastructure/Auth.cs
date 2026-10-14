using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Api.Infrastructure;

public class JwtOptions
{
    public string Issuer { get; set; } = "sammha";
    public string Audience { get; set; } = "sammha-web";
    /// <summary>HMAC-SHA256 signing key, at least 32 characters; keep it in user-secrets or an environment variable, never in source control</summary>
    public string Key { get; set; } = "";
    /// <summary>access token lifetime; the client refreshes silently before it ends</summary>
    public int AccessMinutes { get; set; } = 15;
    /// <summary>refresh token lifetime (sliding: every refresh issues a new one)</summary>
    public int RefreshDays { get; set; } = 30;
}

public static class SammhaClaims
{
    public const string Stamp = "stamp";
    /// <summary>sign-in id (refresh-token family), so "this device" can be told apart in the sessions list</summary>
    public const string Session = "sid";
    /// <summary>delegation id of a delegated session</summary>
    public const string Delegation = "dlg";
}

/// <summary>
/// Signs short-lived access tokens: sub (user id), role, stamp (security stamp), jti, iat.
/// Uses the real clock so tokens stay valid for the JWT middleware.
/// </summary>
public class JwtTokenIssuer(IOptions<JwtOptions> opt) : ITokenIssuer
{
    public TimeSpan RefreshLifetime => TimeSpan.FromDays(Math.Max(1, opt.Value.RefreshDays));

    public AccessToken Issue(User user, Guid sessionId, Guid? delegationId = null)
    {
        var o = opt.Value;
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(Math.Max(1, o.AccessMinutes));
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(SammhaClaims.Stamp, user.SecurityStamp),
            new Claim(SammhaClaims.Session, sessionId.ToString("N")),
        ];
        if (delegationId is { } d) claims.Add(new Claim(SammhaClaims.Delegation, d.ToString("N")));
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, notBefore: now, expires: expires, signingCredentials: creds);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), new DateTimeOffset(expires));
    }
}

public class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;

    public Guid? Id
    {
        get
        {
            var sub = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Stamp => Principal?.FindFirstValue(SammhaClaims.Stamp);
    public string? Ip => http.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => http.HttpContext?.Request.Headers.UserAgent.ToString();
    public Guid? SessionId => Guid.TryParse(Principal?.FindFirstValue(SammhaClaims.Session), out var g) ? g : null;
    public Guid? DelegationId => Guid.TryParse(Principal?.FindFirstValue(SammhaClaims.Delegation), out var g) ? g : null;
}
