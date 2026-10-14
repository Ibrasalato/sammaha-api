namespace Sammha.Domain;

/// <summary>
/// Every platform function a staff member can be allowed to do. Admins (role "admin") have all of them;
/// committee members get the permissions of the staff role the super admin assigned them.
/// </summary>
public static class Permissions
{
    public const string Overview = "overview.view";
    public const string RequestsView = "requests.view";
    public const string RequestsReview = "requests.review";
    public const string RequestsCancel = "requests.cancel";
    public const string AuctionsSchedule = "auctions.schedule";
    public const string AuctionsManage = "auctions.manage";
    public const string AuctionsCancel = "auctions.cancel";
    public const string InvoicesView = "invoices.view";
    public const string InvoicesMarkPaid = "invoices.mark_paid";
    public const string TicketsView = "tickets.view";
    public const string TicketsReply = "tickets.reply";
    public const string TicketsManage = "tickets.manage";
    public const string EventsManage = "events.manage";
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersRoles = "users.roles";
    public const string UsersSuspend = "users.suspend";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string SecurityManage = "security.manage";
    public const string AuditView = "audit.view";

    public record Info(string Key, string Group, string Label, string Description);

    /// <summary>the catalogue, in the order the dashboard shows it</summary>
    public static readonly Info[] Catalog =
    [
        new(Overview, "عام", "النظرة العامة", "لوحة الأرقام والمؤشرات"),
        new(RequestsView, "طلبات الملاك", "عرض الطلبات", "قائمة طلبات الشعارات والأسماء المركبة والاستثمار وتفاصيلها"),
        new(RequestsReview, "طلبات الملاك", "مراجعة طلبات الشعارات", "قبول الأسماء أو رفضها، ورفض الطلب كاملًا"),
        new(RequestsCancel, "طلبات الملاك", "إلغاء الطلبات", "إلغاء طلب مع رد الضمان للمالك"),
        new(AuctionsSchedule, "المزادات", "فتح المزادات", "جدولة الأسماء المقبولة في مزادات"),
        new(AuctionsManage, "المزادات", "التحكم في المزادات", "إيقاف مؤقت واستئناف وتمديد، لمزاد محدد أو لكل المزادات"),
        new(AuctionsCancel, "المزادات", "إلغاء المزادات", "إلغاء مزاد دون بيع وإعادة الاسم لقائمة الجدولة أو لمالكه"),
        new(InvoicesView, "المالية", "عرض الفواتير", "كل فواتير المنصة"),
        new(InvoicesMarkPaid, "المالية", "تأكيد السداد", "تعليم فاتورة مدفوعة يدويًا (تحويل بنكي مثلًا)"),
        new(TicketsView, "الدعم", "عرض الشكاوى والبلاغات", "قائمة التذاكر وتفاصيلها"),
        new(TicketsReply, "الدعم", "الرد على التذاكر", "الرد على العميل وكتابة ملاحظات داخلية"),
        new(TicketsManage, "الدعم", "إدارة التذاكر", "تغيير الحالة والأولوية والإسناد"),
        new(EventsManage, "المحتوى", "المواعيد والفعاليات", "إضافة فعاليات الإبل وتعديلها وحذفها"),
        new(UsersView, "المستخدمون", "عرض المستخدمين", "قائمة الملاك والموظفين"),
        new(UsersCreate, "المستخدمون", "إنشاء مستخدم", "إضافة حسابات موظفين جديدة"),
        new(UsersRoles, "المستخدمون", "تحديد دور الموظف", "تغيير دور الموظف وصلاحياته"),
        new(UsersSuspend, "المستخدمون", "إيقاف الحسابات وتفعيلها", "إيقاف حساب مالك أو موظف وإعادة تفعيله"),
        new(RolesManage, "المستخدمون", "الأدوار والصلاحيات", "إنشاء الأدوار وتحديد صلاحيات كل دور"),
        new(SettingsManage, "الإعدادات", "إعدادات المنصة", "استقبال الطلبات، الإعلان، الصيانة، بيانات التواصل"),
        new(SecurityManage, "الإعدادات", "سياسة الأمان", "كلمات المرور وقفل الدخول ومهلة الخمول وتسجيل خروج الجميع"),
        new(AuditView, "الإعدادات", "سجل التغييرات", "سجل تعديلات الإعدادات والإجراءات الإدارية"),
    ];

    public static readonly string[] All = Catalog.Select(c => c.Key).ToArray();

    public static string Label(string key) => Catalog.FirstOrDefault(c => c.Key == key)?.Label ?? key;

    /// <summary>what the approval committee could do before roles existed</summary>
    public static readonly string[] CommitteeDefault =
        [Overview, RequestsView, RequestsReview, RequestsCancel, AuctionsSchedule, InvoicesView, TicketsView, TicketsReply, TicketsManage, EventsManage, UsersView];

    public static readonly Guid CommitteeRoleId = Guid.Parse("5a3f0000-0000-4000-8000-0000000000c1");
}

/// <summary>A named set of permissions the super admin gives to staff members.</summary>
public class StaffRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>comma-separated permission keys</summary>
    public string Permissions { get; set; } = "";
    /// <summary>the built-in committee role: cannot be deleted</summary>
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public IReadOnlyList<string> PermissionList() =>
        Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Domain.Permissions.All.Contains).ToList();
}
