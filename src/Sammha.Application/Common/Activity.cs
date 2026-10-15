using System.Reflection;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Domain;

namespace Sammha.Application.Common;

/// <summary>writes activity entries through its own database scope, so a failed command's unsaved changes are never saved with the log</summary>
public interface IActivityWriter
{
    Task WriteAsync(ActivityLog entry, CancellationToken ct = default);
}

public static class ActivityCategories
{
    public const string Auth = "auth", Account = "account", Requests = "requests", Auctions = "auctions", Payments = "payments", Finance = "finance",
        Users = "users", Settings = "settings", Tickets = "tickets", Delegation = "delegation", Content = "content", Reports = "reports", Certificates = "certificates", Violations = "violations", Exemptions = "exemptions";
    public static readonly Dictionary<string, string> Arabic = new()
    {
        [Auth] = "الدخول", [Account] = "الحساب", [Requests] = "طلبات الشعارات", [Auctions] = "المزادات", [Payments] = "المدفوعات", [Finance] = "المالية",
        [Users] = "المستخدمون والأدوار", [Settings] = "الإعدادات", [Tickets] = "الشكاوى والبلاغات", [Delegation] = "التفويض", [Content] = "المحتوى", [Reports] = "التقارير", [Certificates] = "الشهادات", [Violations] = "المخالفات", [Exemptions] = "استثناءات المشاركة",
    };
}

/// <summary>the Arabic label and category of each command in the activity log</summary>
public static class ActivityLabels
{
    public static readonly Dictionary<string, (string Label, string Category)> Map = new()
    {
        ["StartSignInCommand"] = ("محاولة دخول", ActivityCategories.Auth), ["CompleteSignInCommand"] = ("تسجيل دخول", ActivityCategories.Auth),
        ["SignOutCommand"] = ("تسجيل خروج", ActivityCategories.Auth), ["StartSignUpCommand"] = ("بدء إنشاء حساب", ActivityCategories.Auth),
        ["SignUpCommand"] = ("إنشاء حساب مالك", ActivityCategories.Auth), ["ResendOtpCommand"] = ("إعادة إرسال رمز التحقق", ActivityCategories.Auth),
        ["ChangePasswordCommand"] = ("تغيير كلمة المرور", ActivityCategories.Account), ["RevokeMySessionCommand"] = ("إخراج جهاز", ActivityCategories.Account),
        ["UpdateAccountCommand"] = ("تعديل بيانات الحساب", ActivityCategories.Account), ["UpdateProfileCommand"] = ("تعديل الصورة الشخصية", ActivityCategories.Account),
        ["UpdatePersonalCommand"] = ("إكمال البيانات الشخصية", ActivityCategories.Account), ["UpdateAddressCommand"] = ("تعديل العنوان", ActivityCategories.Account),
        ["RequestEmailChangeCommand"] = ("طلب تغيير البريد", ActivityCategories.Account), ["ConfirmEmailChangeCommand"] = ("تغيير البريد", ActivityCategories.Account),
        ["RequestPhoneChangeCommand"] = ("طلب تغيير الجوال", ActivityCategories.Account), ["ConfirmPhoneChangeCommand"] = ("تغيير الجوال", ActivityCategories.Account),
        ["RequestEmailVerificationCommand"] = ("طلب توثيق البريد", ActivityCategories.Account), ["ConfirmEmailVerificationCommand"] = ("توثيق البريد", ActivityCategories.Account),
        ["UpdateMyPreferencesCommand"] = ("تعديل الإشعارات", ActivityCategories.Account), ["SaveMyBankAccountCommand"] = ("حفظ الحساب البنكي", ActivityCategories.Finance),
        ["CreateRequestCommand"] = ("تقديم طلب شعار", ActivityCategories.Requests), ["PayDepositCommand"] = ("سداد ضمان طلب", ActivityCategories.Requests),
        ["CancelMyRequestCommand"] = ("إلغاء طلب من المالك", ActivityCategories.Requests), ["WithdrawNameCommand"] = ("سحب اسم من طلب", ActivityCategories.Requests),
        ["CreateCompoundRequestCommand"] = ("طلب اسم مركب", ActivityCategories.Requests), ["CreateInvestmentRequestCommand"] = ("طلب استثمار اسم", ActivityCategories.Requests),
        ["ReviewRequestCommand"] = ("اعتماد قرار طلب", ActivityCategories.Requests), ["RejectRequestCommand"] = ("رفض طلب", ActivityCategories.Requests),
        ["CancelRequestCommand"] = ("إلغاء طلب من الإدارة", ActivityCategories.Requests),
        ["AuctionIcoNameCommand"] = ("طرح اسم للمنظمة في مزاد", ActivityCategories.Auctions),
        ["CreateTransferCommand"] = ("طلب نقل ملكية اسم", ActivityCategories.Requests), ["CancelTransferCommand"] = ("إلغاء طلب نقل ملكية", ActivityCategories.Requests), ["PayTransferFeeCommand"] = ("سداد رسوم نقل ملكية", ActivityCategories.Payments),
        ["RespondTransferCommand"] = ("رد المستلم على نقل ملكية", ActivityCategories.Requests), ["ReviewTransferCommand"] = ("مراجعة نقل ملكية", ActivityCategories.Requests),
        ["ScheduleAuctionsCommand"] = ("فتح مزادات", ActivityCategories.Auctions), ["PauseAuctionsCommand"] = ("إيقاف مزادات مؤقتًا", ActivityCategories.Auctions),
        ["ResumeAuctionsCommand"] = ("استئناف مزادات", ActivityCategories.Auctions), ["ExtendAuctionsCommand"] = ("تمديد مزادات", ActivityCategories.Auctions),
        ["CancelAuctionsCommand"] = ("إلغاء مزادات", ActivityCategories.Auctions), ["PlaceBidCommand"] = ("مزايدة", ActivityCategories.Auctions),
        ["SetAutoBidCommand"] = ("تفعيل مزايدة آلية", ActivityCategories.Auctions), ["CancelAutoBidCommand"] = ("إيقاف مزايدة آلية", ActivityCategories.Auctions),
        ["ToggleWatchCommand"] = ("متابعة مزاد", ActivityCategories.Auctions), ["PayBidDepositCommand"] = ("سداد ضمان دخول مزاد", ActivityCategories.Auctions),
        ["CreateListingCommand"] = ("عرض اسم للبيع", ActivityCategories.Auctions), ["CancelListingCommand"] = ("إلغاء عرض بيع", ActivityCategories.Auctions),
        ["BuyListingCommand"] = ("شراء اسم", ActivityCategories.Auctions),
        ["TopUpCommand"] = ("شحن المحفظة", ActivityCategories.Payments), ["StartCardPaymentCommand"] = ("بدء دفع بالبطاقة", ActivityCategories.Payments),
        ["PayInvoiceCommand"] = ("سداد فاتورة", ActivityCategories.Payments), ["MarkInvoicePaidCommand"] = ("تسجيل سداد فاتورة", ActivityCategories.Payments),
        ["MoyasarWebhookCommand"] = ("إشعار بوابة الدفع", ActivityCategories.Payments),
        ["RequestWithdrawalCommand"] = ("طلب استرداد للبنك", ActivityCategories.Finance), ["CompleteWithdrawalCommand"] = ("تنفيذ استرداد", ActivityCategories.Finance),
        ["RejectWithdrawalCommand"] = ("رفض استرداد", ActivityCategories.Finance), ["PayoutWithdrawalCommand"] = ("تحويل استرداد مباشر للبنك", ActivityCategories.Finance),
        ["UploadLogoCommand"] = ("رفع لوجو شعار", ActivityCategories.Account), ["DeleteLogoCommand"] = ("حذف لوجو شعار", ActivityCategories.Account), ["ReviewLogoCommand"] = ("مراجعة لوجو شعار", ActivityCategories.Requests), ["DirectRefundCommand"] = ("استرداد مباشر للبنك", ActivityCategories.Finance),
        ["AdjustWalletCommand"] = ("تسوية رصيد محفظة", ActivityCategories.Finance), ["ReviewBankAccountCommand"] = ("مراجعة حساب بنكي", ActivityCategories.Finance),
        ["RefundDepositCommand"] = ("استرداد ضمان", ActivityCategories.Finance),
        ["CreateStaffCommand"] = ("إضافة موظف", ActivityCategories.Users), ["SetUserRoleCommand"] = ("تغيير نوع حساب", ActivityCategories.Users),
        ["SetUserStaffRoleCommand"] = ("تغيير دور موظف", ActivityCategories.Users), ["SetUserActiveCommand"] = ("إيقاف أو تفعيل حساب", ActivityCategories.Users),
        ["SaveStaffRoleCommand"] = ("حفظ دور", ActivityCategories.Users), ["DeleteStaffRoleCommand"] = ("حذف دور", ActivityCategories.Users),
        ["UpdateSettingsCommand"] = ("تعديل إعدادات المنصة", ActivityCategories.Settings), ["SignOutEveryoneCommand"] = ("تسجيل خروج الجميع", ActivityCategories.Settings),
        ["SaveTicketRoutingCommand"] = ("توزيع الشكاوى والبلاغات", ActivityCategories.Settings),
        ["SubmitTicketCommand"] = ("تقديم شكوى أو استفسار", ActivityCategories.Tickets), ["FollowUpTicketCommand"] = ("متابعة تذكرة", ActivityCategories.Tickets),
        ["ReplyTicketCommand"] = ("رد على تذكرة", ActivityCategories.Tickets), ["UpdateTicketCommand"] = ("تحديث تذكرة", ActivityCategories.Tickets),
        ["SaveEventCommand"] = ("حفظ فعالية", ActivityCategories.Content), ["DeleteEventCommand"] = ("حذف فعالية", ActivityCategories.Content),
        ["LogOwnersReportCommand"] = ("طباعة أو تصدير بيانات العملاء", ActivityCategories.Reports),
        ["IssueCertificateCommand"] = ("إصدار شهادة", ActivityCategories.Certificates), ["RevokeCertificateCommand"] = ("إلغاء شهادة", ActivityCategories.Certificates),
        ["SetLatinNameCommand"] = ("تعديل الكتابة اللاتينية لاسم", ActivityCategories.Certificates),
        ["LinkCamelCommand"] = ("ربط اسم بشريحة", ActivityCategories.Account), ["UnlinkCamelCommand"] = ("فك ربط اسم بشريحة", ActivityCategories.Account),
        ["SaveViolationTypeCommand"] = ("حفظ نوع مخالفة", ActivityCategories.Settings), ["DeleteViolationTypeCommand"] = ("حذف نوع مخالفة", ActivityCategories.Settings),
        ["IssueViolationCommand"] = ("إصدار مخالفة", ActivityCategories.Violations), ["CancelViolationCommand"] = ("إلغاء مخالفة", ActivityCategories.Violations),
        ["PayViolationCommand"] = ("سداد مخالفة", ActivityCategories.Payments),
        ["SaveFestivalCommand"] = ("حفظ مهرجان", ActivityCategories.Settings), ["DeleteFestivalCommand"] = ("حذف مهرجان", ActivityCategories.Settings),
        ["ResolveViolationByNameCommand"] = ("تسوية مخالفة بتسجيل اسم", ActivityCategories.Violations), ["BanViolationCommand"] = ("منع من المشاركة بسبب مخالفة", ActivityCategories.Violations),
        ["RequestExemptionCommand"] = ("طلب استثناء مشاركة بدون اسم", ActivityCategories.Exemptions), ["CancelMyExemptionCommand"] = ("سحب طلب استثناء", ActivityCategories.Exemptions),
        ["PriceExemptionCommand"] = ("تحديد غرامة استثناء", ActivityCategories.Exemptions), ["RejectExemptionCommand"] = ("رفض طلب استثناء", ActivityCategories.Exemptions),
        ["RevokeExemptionCommand"] = ("إلغاء وثيقة استثناء", ActivityCategories.Exemptions), ["PayExemptionCommand"] = ("سداد غرامة استثناء", ActivityCategories.Payments),
        ["SaveDelegationCommand"] = ("حفظ تفويض", ActivityCategories.Delegation), ["RevokeDelegationCommand"] = ("إلغاء تفويض", ActivityCategories.Delegation),
        ["ActAsCommand"] = ("الدخول بتفويض", ActivityCategories.Delegation), ["StopActingCommand"] = ("الخروج من التفويض", ActivityCategories.Delegation),
    };
    /// <summary>too frequent to be useful in the log</summary>
    public static readonly HashSet<string> Skip = ["RefreshSessionCommand", "MarkNotificationsReadCommand"];

    /// <summary>never written to the log: credentials, and personal data (identity numbers, contact details, birth dates, sign-in names)</summary>
    private static readonly string[] Secret = ["password", "code", "challenge", "token", "base64", "iban", "card", "secret", "next", "current",
        "nationalid", "phone", "email", "birth", "login"];

    /// <summary>the command's values without secrets, for the details column</summary>
    public static string Describe(object request)
    {
        var sb = new StringBuilder();
        void Add(string name, object? v, int depth)
        {
            if (v == null || depth > 2) return;
            if (Secret.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) return;
            var t = v.GetType();
            if (t == typeof(string) || t.IsPrimitive || t.IsEnum || v is decimal or Guid or DateTimeOffset or DateTime or DateOnly)
            {
                var s = v is DateTimeOffset d ? d.ToString("yyyy-MM-dd HH:mm") : v.ToString();
                if (string.IsNullOrWhiteSpace(s)) return;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(name).Append(": ").Append(s.Length > 80 ? s[..80] + "…" : s);
            }
            else if (v is System.Collections.IEnumerable list)
            {
                var items = list.Cast<object?>().Take(6).Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x) && x!.Length < 60).ToList();
                if (items.Count > 0) { if (sb.Length > 0) sb.Append(" · "); sb.Append(name).Append(": ").Append(string.Join("، ", items)); }
            }
            else foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) Add(p.Name, p.GetValue(v), depth + 1);
        }
        foreach (var p in request.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) Add(p.Name, p.GetValue(request), 0);
        var text = sb.ToString();
        return text.Length > 480 ? text[..480] + "…" : text;
    }
}

/// <summary>Logs every command: who, when, from where, on whose behalf, and whether it worked.</summary>
public class ActivityBehavior<TRequest, TResponse>(ICurrentUser current, IAppDbContext db, IActivityWriter writer, TimeProvider clock, Access access)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        if (!name.EndsWith("Command") || ActivityLabels.Skip.Contains(name)) return await next();
        var at = clock.GetUtcNow();
        try
        {
            var result = await next();
            await LogAsync(name, request, result, null, at);
            return result;
        }
        catch (DomainException e)
        {
            await LogAsync(name, request, default, e.Message, at);
            throw;
        }
    }

    private async Task LogAsync(string name, TRequest request, TResponse? result, string? error, DateTimeOffset at)
    {
        try
        {
            var (label, cat) = ActivityLabels.Map.TryGetValue(name, out var m) ? m : (name, ActivityCategories.Account);
            Guid? actorId = current.Id;
            string actorName = "", role = "";
            Guid? behalfId = null; string? behalfName = null;
            if (result is AuthResult ar) { actorId = ar.User.Id; actorName = ar.User.FullName; role = ar.User.Role; }
            else if (actorId is { } id)
            {
                var u = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.FullName, x.Role }).FirstOrDefaultAsync();
                actorName = u?.FullName ?? ""; role = u?.Role ?? "";
            }
            // a delegate acting on an owner's account: the delegate is the actor
            if (current.DelegationId is { } did && result is not AuthResult)
            {
                var d = await access.DelegationAsync(CancellationToken.None);
                if (d != null)
                {
                    behalfId = d.OwnerId; behalfName = actorName;
                    actorId = d.DelegateUserId;
                    actorName = await db.Users.AsNoTracking().Where(x => x.Id == d.DelegateUserId).Select(x => x.FullName).FirstOrDefaultAsync() ?? d.FullName;
                    role = "delegate";
                }
            }
            if (actorName == "" && request.GetType().GetProperty("Login")?.GetValue(request) is string login) actorName = login.Length > 3 ? login[..3] + "•••" : login;
            await writer.WriteAsync(new ActivityLog
            {
                At = at, ActorId = actorId, ActorName = actorName, ActorRole = role, OnBehalfOfId = behalfId, OnBehalfOfName = behalfName,
                Action = label, Category = cat, Details = ActivityLabels.Describe(request), Success = error == null, Error = error,
                Ip = current.Ip, UserAgent = current.UserAgent is { Length: > 200 } ua ? ua[..200] : current.UserAgent,
            });
        }
        catch { /* the log never breaks the action */ }
    }
}

/// <summary>In a delegated session only the granted scopes go through; account, security, bank and delegation stay with the owner.</summary>
public class DelegationGuardBehavior<TRequest, TResponse>(ICurrentUser current, Access access) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (current.DelegationId == null) return await next();
        var name = typeof(TRequest).Name;
        if (name.EndsWith("Query"))
        {
            if (DelegationScopes.HiddenQueries.Contains(name)) throw DomainException.Forbidden("هذه البيانات لصاحب الحساب فقط");
            return await next();
        }
        if (DelegationScopes.Always.Contains(name)) return await next();
        var d = await access.DelegationAsync(ct) ?? throw DomainException.Unauthorized("انتهى التفويض، سجّل الدخول من جديد");
        if (!DelegationScopes.ByCommand.TryGetValue(name, out var scope)) throw DomainException.Forbidden("هذا الإجراء لصاحب الحساب فقط");
        if (!d.ScopeList().Contains(scope)) throw DomainException.Forbidden($"التفويض لا يشمل «{DelegationScopes.Label(scope)}»");
        return await next();
    }
}
