using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sammha.Domain;

namespace Sammha.Application.Common;

/// <summary>
/// Sign-in sessions: a short-lived JWT access token plus a rotating refresh token.
/// Refresh tokens are random 256-bit values; only their SHA-256 hash is stored. Each refresh rotates the token,
/// and presenting an already-rotated token (a stolen copy) revokes the whole family.
/// </summary>
public class Sessions(IAppDbContext db, TimeProvider clock, ITokenIssuer tokens, ICurrentUser current)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task<AuthResult> IssueAsync(User u, Guid? family = null, CancellationToken ct = default)
    {
        var refresh = NewToken();
        var row = new RefreshToken
        {
            UserId = u.Id, TokenHash = Hash(refresh), FamilyId = family ?? Guid.NewGuid(), CreatedAt = Now, ExpiresAt = Now + tokens.RefreshLifetime,
            CreatedByIp = Clip(current.Ip, 64), UserAgent = Clip(current.UserAgent, 300),
        };
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(ct);
        var access = tokens.Issue(u);
        return new AuthResult(Mapper.User(u), access.Token, access.ExpiresAt, refresh, row.ExpiresAt);
    }

    public async Task<AuthResult> RefreshAsync(string token, CancellationToken ct)
    {
        var expired = DomainException.Unauthorized("انتهت الجلسة، سجّل الدخول من جديد");
        if (string.IsNullOrWhiteSpace(token)) throw expired;
        await using var uow = await db.BeginAsync([$"rt:{Hash(token)}"], ct);
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == Hash(token), ct) ?? throw expired;
        if (row.RevokedAt != null)
        {
            if (row.ReplacedById != null)
            {
                // a rotated token came back: someone has a copy. End every session of this sign-in.
                await RevokeFamilyAsync(row.FamilyId, "reuse", ct);
                await db.SaveChangesAsync(ct);
                await uow.CommitAsync(ct);
            }
            throw expired;
        }
        if (Now >= row.ExpiresAt) throw expired;
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == row.UserId, ct);
        if (u == null || !u.Active) throw DomainException.Unauthorized("هذا الحساب موقوف");
        var next = await IssueAsync(u, row.FamilyId, ct);
        row.RevokedAt = Now;
        row.RevokedReason = "rotated";
        row.ReplacedById = (await db.RefreshTokens.FirstAsync(x => x.TokenHash == Hash(next.RefreshToken), ct)).Id;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return next;
    }

    /// <summary>sign out of this device</summary>
    public async Task RevokeAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == Hash(token), ct);
        if (row == null || row.RevokedAt != null) return;
        row.RevokedAt = Now;
        row.RevokedReason = "signout";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>sign out everywhere: every refresh token revoked and every access token invalid at once (new security stamp)</summary>
    public async Task RevokeAllAsync(User u, string reason, CancellationToken ct)
    {
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = Now;
        await db.RefreshTokens.Where(x => x.UserId == u.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    private async Task RevokeFamilyAsync(Guid family, string reason, CancellationToken ct)
    {
        var now = Now;
        await db.RefreshTokens.Where(x => x.FamilyId == family && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    private static string? Clip(string? s, int max) => s == null ? null : s.Length > max ? s[..max] : s;
}
