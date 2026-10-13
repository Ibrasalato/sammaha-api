using MediatR;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Auth;

/// <summary>Sign-up, two-step sign-in (password then one-time code) and account changes.</summary>
public partial class AuthHandlers(IAppDbContext db, TimeProvider clock, Access access, IPasswordHasher hasher, ITokenIssuer tokens, ISmsSender sms,
    IOptions<OtpOptions> otpOpt)
    : IRequestHandler<GetCurrentUserQuery, UserDto?>, IRequestHandler<SignUpCommand, AuthResult>, IRequestHandler<StartSignInCommand, OtpChallengeDto>, IRequestHandler<CompleteSignInCommand, AuthResult>, IRequestHandler<ResendOtpCommand, OtpChallengeDto>, IRequestHandler<ChangePasswordCommand, MediatR.Unit>, IRequestHandler<UpdateProfileCommand, UserDto>, IRequestHandler<UpdateAccountCommand, UserDto>, IRequestHandler<RequestEmailChangeCommand, OtpChallengeDto>, IRequestHandler<ConfirmEmailChangeCommand, UserDto>, IRequestHandler<RequestPhoneChangeCommand, OtpChallengeDto>, IRequestHandler<ConfirmPhoneChangeCommand, UserDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private OtpOptions Otp => otpOpt.Value;
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")] private static partial Regex EmailRx();
    [GeneratedRegex(@"^[12]\d{9}$")] private static partial Regex NationalIdRx();

    private async Task<UserDto?> CurrentAsync(CancellationToken ct) => (await access.MaybeAsync(ct)) is { } u ? Mapper.User(u) : null;

    private async Task<AuthResult> SignUpAsync(SignUpInput i, CancellationToken ct)
    {
        var phone = NameMatcher.NormalizePhone(i.Phone);
        var email = (i.Email ?? "").Trim();
        if (!NameMatcher.IsSaudiMobile(phone)) throw new DomainException("رقم الجوال يبدأ بـ 05 ويتكون من 10 أرقام");
        if ((i.FullName ?? "").Trim().Length < 3) throw new DomainException("اكتب الاسم الرباعي");
        if (!EmailRx().IsMatch(email)) throw new DomainException("البريد الإلكتروني غير صحيح");
        if ((i.Password ?? "").Length < 6) throw new DomainException("كلمة المرور 6 أحرف على الأقل");
        var nid = i.NationalId?.Trim();
        if (!string.IsNullOrEmpty(nid) && !NationalIdRx().IsMatch(nid)) throw new DomainException("رقم الهوية أو الإقامة 10 أرقام يبدأ بـ 1 أو 2");
        if (await db.Users.AnyAsync(u => u.Phone == phone, ct)) throw new DomainException("رقم الجوال مسجل مسبقًا، سجّل الدخول");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new DomainException("البريد الإلكتروني مسجل مسبقًا");
        var u = new User { FullName = i.FullName!.Trim(), Phone = phone, Email = email, NationalId = string.IsNullOrEmpty(nid) ? null : nid, Role = Roles.Owner, CreatedAt = Now };
        u.PasswordHash = hasher.Hash(i.Password!);
        db.Users.Add(u);
        await db.SaveChangesAsync(ct);
        return new AuthResult(Mapper.User(u), tokens.Issue(u));
    }

    /// <summary>Step 1. The same generic error whichever portal is wrong, so the staff portal is never revealed.</summary>
    private async Task<OtpChallengeDto> StartSignInAsync(string login, string password, string portal, CancellationToken ct)
    {
        var l = (login ?? "").Trim().ToLowerInvariant();
        var phone = NameMatcher.NormalizePhone(l);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Phone == phone || x.Email == l, ct);
        var wrong = new DomainException(portal == "staff" ? "البيانات غير صحيحة" : "رقم الجوال أو كلمة المرور غير صحيحة");
        if (u == null || (portal == "staff") != Roles.IsStaff(u.Role)) throw wrong;
        if (!hasher.Verify(u.PasswordHash, password ?? "")) throw wrong;
        if (!u.Active) throw new DomainException("هذا الحساب موقوف، تواصل مع مدير النظام");
        return await IssueOtpAsync(u.Id, OtpPurposes.Login, u.Phone, ct);
    }

    private async Task<AuthResult> CompleteSignInAsync(string challenge, string code, CancellationToken ct)
    {
        var c = await ConsumeOtpAsync(challenge, code, OtpPurposes.Login, ct);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == c.UserId, ct);
        if (u == null || !u.Active) throw new DomainException("تعذّر الدخول");
        return new AuthResult(Mapper.User(u), tokens.Issue(u));
    }

    private async Task<OtpChallengeDto> ResendOtpAsync(string challenge, CancellationToken ct)
    {
        var c = Guid.TryParse(challenge, out var id) ? await db.OtpChallenges.FirstOrDefaultAsync(x => x.Id == id && !x.Used, ct) : null;
        if (c == null) throw new DomainException("انتهت الجلسة، أعد إدخال بياناتك");
        c.Used = true;
        return await IssueOtpAsync(c.UserId, c.Purpose, c.Target, ct);
    }

    private async Task ChangePasswordAsync(string current, string next, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (!hasher.Verify(u.PasswordHash, current ?? "")) throw new DomainException("كلمة المرور الحالية غير صحيحة");
        if ((next ?? "").Length < 8) throw new DomainException("كلمة المرور الجديدة 8 أحرف على الأقل");
        u.PasswordHash = hasher.Hash(next!);
        await db.SaveChangesAsync(ct);
    }

    // ───── account ─────
    private async Task<UserDto> UpdateProfileAsync(string? avatarUrl, bool avatarGiven, bool showAvatar, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (avatarGiven)
        {
            if (avatarUrl != null && (avatarUrl.Length > 1_500_000 || !avatarUrl.StartsWith("data:image/"))) throw new DomainException("الصورة كبيرة أو غير صالحة");
            u.AvatarUrl = avatarUrl;
        }
        u.ShowAvatar = showAvatar && u.AvatarUrl != null;
        await db.SaveChangesAsync(ct);
        return Mapper.User(u);
    }

    private async Task<UserDto> UpdateAccountAsync(string fullName, string? nationalId, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var name = Regex.Replace((fullName ?? "").Trim(), @"\s+", " ");
        if (name.Length < 6) throw new DomainException("اكتب الاسم الرباعي");
        var nid = nationalId?.Trim();
        if (!string.IsNullOrEmpty(nid) && !NationalIdRx().IsMatch(nid)) throw new DomainException("رقم الهوية أو الإقامة 10 أرقام يبدأ بـ 1 أو 2");
        if (name != u.FullName)
            await db.ReservedNames.Where(r => r.OwnerId == u.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.OwnerName, name), ct);
        u.FullName = name;
        u.NationalId = string.IsNullOrEmpty(nid) ? null : nid;
        await db.SaveChangesAsync(ct);
        return Mapper.User(u);
    }

    private async Task<OtpChallengeDto> RequestEmailChangeAsync(string newEmail, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var mail = (newEmail ?? "").Trim();
        if (!EmailRx().IsMatch(mail)) throw new DomainException("البريد الإلكتروني غير صحيح");
        if (string.Equals(mail, u.Email, StringComparison.OrdinalIgnoreCase)) throw new DomainException("هذا بريدك الحالي");
        if (access.IsSuper(u)) throw new DomainException("بريد مدير النظام الرئيسي ثابت");
        if (await db.Users.AnyAsync(x => x.Email == mail, ct)) throw new DomainException("البريد الإلكتروني مسجل لحساب آخر");
        return await IssueOtpAsync(u.Id, OtpPurposes.Email, mail, ct);
    }

    private async Task<UserDto> ConfirmEmailChangeAsync(string challenge, string code, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var c = await ConsumeOtpAsync(challenge, code, OtpPurposes.Email, ct);
        if (c.UserId != u.Id) throw new DomainException("تعذّر التحقق");
        if (await db.Users.AnyAsync(x => x.Id != u.Id && x.Email == c.Target, ct)) throw new DomainException("البريد الإلكتروني مسجل لحساب آخر");
        u.Email = c.Target;
        await db.SaveChangesAsync(ct);
        return Mapper.User(u);
    }

    private async Task<OtpChallengeDto> RequestPhoneChangeAsync(string newPhone, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var phone = NameMatcher.NormalizePhone(newPhone);
        if (!NameMatcher.IsSaudiMobile(phone)) throw new DomainException("رقم الجوال يبدأ بـ 05 ويتكون من 10 أرقام");
        if (phone == u.Phone) throw new DomainException("هذا رقمك الحالي");
        if (await db.Users.AnyAsync(x => x.Phone == phone, ct)) throw new DomainException("رقم الجوال مسجل لحساب آخر");
        return await IssueOtpAsync(u.Id, OtpPurposes.Phone, phone, ct);
    }

    private async Task<UserDto> ConfirmPhoneChangeAsync(string challenge, string code, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var c = await ConsumeOtpAsync(challenge, code, OtpPurposes.Phone, ct);
        if (c.UserId != u.Id) throw new DomainException("تعذّر التحقق");
        if (await db.Users.AnyAsync(x => x.Id != u.Id && x.Phone == c.Target, ct)) throw new DomainException("رقم الجوال مسجل لحساب آخر");
        u.Phone = c.Target;
        await db.SaveChangesAsync(ct);
        return Mapper.User(u);
    }

    // ───── one-time codes ─────
    private async Task<OtpChallengeDto> IssueOtpAsync(Guid userId, string purpose, string target, CancellationToken ct)
    {
        var code = string.IsNullOrEmpty(Otp.FixedCode)
            ? RandomNumberGenerator.GetInt32((int)Math.Pow(10, Otp.Length - 1), (int)Math.Pow(10, Otp.Length)).ToString()
            : Otp.FixedCode;
        var c = new OtpChallenge { UserId = userId, Purpose = purpose, Target = target, CodeHash = hasher.Hash(code), ExpiresAt = Now.AddMinutes(Otp.TtlMinutes) };
        db.OtpChallenges.Add(c);
        await db.SaveChangesAsync(ct);
        if (string.IsNullOrEmpty(Otp.FixedCode)) await sms.SendCodeAsync(target, code, purpose);
        return new OtpChallengeDto(c.Id.ToString(), purpose == OtpPurposes.Email ? MaskEmail(target) : MaskPhone(target), c.ExpiresAt, code.Length);
    }

    /// <summary>A wrong code is counted and saved (not rolled back), so guessing stops after MaxAttempts.</summary>
    private async Task<OtpChallenge> ConsumeOtpAsync(string challenge, string code, string purpose, CancellationToken ct)
    {
        var c = Guid.TryParse(challenge, out var id) ? await db.OtpChallenges.FirstOrDefaultAsync(x => x.Id == id, ct) : null;
        if (c == null || c.Used || c.Purpose != purpose || Now > c.ExpiresAt) throw new DomainException("انتهت صلاحية الرمز، اطلب رمزًا جديدًا");
        if (c.Attempts >= Otp.MaxAttempts) { c.Used = true; await db.SaveChangesAsync(ct); throw new DomainException("تجاوزت عدد المحاولات، اطلب رمزًا جديدًا"); }
        c.Attempts += 1;
        var digits = NameMatcher.NormalizePhone(code); // keeps digits only (Arabic digits too)
        if (!hasher.Verify(c.CodeHash, digits))
        {
            await db.SaveChangesAsync(ct);
            throw new DomainException($"الرمز غير صحيح، بقي {Otp.MaxAttempts - c.Attempts} محاولات");
        }
        c.Used = true;
        await db.SaveChangesAsync(ct);
        return c;
    }

    public static string MaskPhone(string p) => p.Length < 7 ? p : $"{p[..2]}•••••{p[^3..]}";
    public static string MaskEmail(string e)
    {
        var at = e.IndexOf('@');
        return at <= 0 ? e : $"{e[0]}•••{e[at..]}";
    }

    // ───── MediatR entry points ─────
    public Task<UserDto?> Handle(GetCurrentUserQuery q, CancellationToken ct) => CurrentAsync(ct);
    public Task<AuthResult> Handle(SignUpCommand q, CancellationToken ct) => SignUpAsync(q.Input, ct);
    public Task<OtpChallengeDto> Handle(StartSignInCommand q, CancellationToken ct) => StartSignInAsync(q.Login, q.Password, q.Portal, ct);
    public Task<AuthResult> Handle(CompleteSignInCommand q, CancellationToken ct) => CompleteSignInAsync(q.Challenge, q.Code, ct);
    public Task<OtpChallengeDto> Handle(ResendOtpCommand q, CancellationToken ct) => ResendOtpAsync(q.Challenge, ct);
    public async Task<MediatR.Unit> Handle(ChangePasswordCommand q, CancellationToken ct) { await ChangePasswordAsync(q.Current, q.Next, ct); return Unit.Value; }
    public Task<UserDto> Handle(UpdateProfileCommand q, CancellationToken ct) => UpdateProfileAsync(q.AvatarUrl, q.AvatarSet, q.ShowAvatar, ct);
    public Task<UserDto> Handle(UpdateAccountCommand q, CancellationToken ct) => UpdateAccountAsync(q.FullName, q.NationalId, ct);
    public Task<OtpChallengeDto> Handle(RequestEmailChangeCommand q, CancellationToken ct) => RequestEmailChangeAsync(q.Email, ct);
    public Task<UserDto> Handle(ConfirmEmailChangeCommand q, CancellationToken ct) => ConfirmEmailChangeAsync(q.Challenge, q.Code, ct);
    public Task<OtpChallengeDto> Handle(RequestPhoneChangeCommand q, CancellationToken ct) => RequestPhoneChangeAsync(q.Phone, ct);
    public Task<UserDto> Handle(ConfirmPhoneChangeCommand q, CancellationToken ct) => ConfirmPhoneChangeAsync(q.Challenge, q.Code, ct);
}
