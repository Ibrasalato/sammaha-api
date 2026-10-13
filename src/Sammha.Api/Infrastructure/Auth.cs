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
    /// <summary>at least 32 characters; keep it in user-secrets or an environment variable, never in source control</summary>
    public string Key { get; set; } = "";
    public int Hours { get; set; } = 12;
}

/// <summary>uses the real clock (not the app TimeProvider) so tokens stay valid for the JWT middleware</summary>
public class JwtTokenIssuer(IOptions<JwtOptions> opt) : ITokenIssuer
{
    public string Issue(User user)
    {
        var o = opt.Value;
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role)],
            notBefore: now, expires: now.AddHours(o.Hours), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid? Id
    {
        get
        {
            var u = http.HttpContext?.User;
            var sub = u?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? u?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }
}
