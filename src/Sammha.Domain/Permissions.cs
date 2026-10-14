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
    public const string RequestsAudit = "requests.audit";
    public const string OwnersView = "owners.view";
    public const string OwnersFull = "owners.full";
    public const string ReportsMarks = "reports.marks";
    public const string WalletsView = "wallets.view";
    public const string WalletsRefund = "wallets.refund";
    public const string WalletsAdjust = "wallets.adjust";
    public const string BanksVerify = "banks.verify";
    public const string DepositsView = "deposits.view";
    public const string DepositsRefund = "deposits.refund";
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
    public const string LogsView = "logs.view";
    public const string CertificatesManage = "certificates.manage";
    public const string NamesLatin = "names.latin";
    public const string NamesIco = "names.ico";
    public const string NamesTransfer = "names.transfer";
    public const string NamesLogos = "names.logos";
    public const string ViolationsView = "violations.view";
    public const string ViolationsManage = "violations.manage";

    public record Info(string Key, string Group, string Label, string Description);

    /// <summary>the catalogue, in the order the dashboard shows it</summary>
    public static readonly Info[] Catalog =
    [
        new(Overview, "عام", "النظرة العامة", "لوحة الأرقام والمؤشرات"),
        new(RequestsView, "طلبات الملاك", "عرض الطلبات", "قائمة طلبات الشعارات والأسماء المركبة والاستثمار وتفاصيلها"),
        new(RequestsReview, "طلبات الملاك", "مراجعة طلبات الشعارات", "قبول الأسماء أو رفضها، ورفض الطلب كاملًا"),
        new(RequestsCancel, "طلبات الملاك", "إلغاء الطلبات", "إلغاء طلب مع رد الضمان للمالك"),
        new(RequestsAudit, "طلبات الملاك", "تقرير تدقيق الشعارات", "المطابق والمشابه (بـ«ال» أو بدونها، جمع أو مفرد) وأعلى سعر لشعار مشابه، مع الطباعة"),
        new(AuctionsSchedule, "المزادات", "فتح المزادات", "جدولة الأسماء المقبولة في مزادات"),
        new(AuctionsManage, "المزادات", "التحكم في المزادات", "إيقاف مؤقت واستئناف وتمديد، لمزاد محدد أو لكل المزادات"),
        new(AuctionsCancel, "المزادات", "إلغاء المزادات", "إلغاء مزاد دون بيع وإعادة الاسم لقائمة الجدولة أو لمالكه"),
        new(InvoicesView, "المالية", "عرض الفواتير", "كل فواتير المنصة"),
        new(InvoicesMarkPaid, "المالية", "تأكيد السداد", "تعليم فاتورة مدفوعة يدويًا (تحويل بنكي مثلًا)"),
        new(WalletsView, "المالية", "تقرير المحافظ", "أرصدة محافظ الملاك وحركاتها وطلبات الاسترداد"),
        new(WalletsRefund, "المالية", "الاسترداد للحساب البنكي", "تنفيذ طلبات الاسترداد أو رفضها، واسترداد مباشر برقم التحويل"),
        new(BanksVerify, "المالية", "اعتماد الحسابات البنكية", "مراجعة شهادة الآيبان التي رفعها المالك واعتماد حسابه أو رفضه"),
        new(WalletsAdjust, "المالية", "تسوية رصيد المحفظة", "إضافة مبلغ أو خصمه من محفظة مالك مع ذكر السبب"),
        new(DepositsView, "المالية", "تقرير الضمانات", "ضمانات طلبات الشعارات وضمانات دخول مزادات إعادة البيع"),
        new(DepositsRefund, "المالية", "استرداد الضمان", "إعادة ضمان محجوز إلى محفظة صاحبه مع ذكر السبب"),
        new(CertificatesManage, "سجل الأسماء", "الشهادات الصادرة", "كل الشهادات التي أصدرها الملاك، والتحقق منها، وإلغاء شهادة مع ذكر السبب"),
        new(NamesIco, "سجل الأسماء", "أسماء المنظمة الدولية", "الأسماء التي آلت إلى المنظمة لعدم سداد الفائز في المهلة، وطرحها في مزاد جديد"),
        new(NamesTransfer, "سجل الأسماء", "طلبات نقل الملكية", "مراجعة طلبات نقل ملكية اسم من مالك إلى آخر واعتمادها أو رفضها"),
        new(NamesLogos, "سجل الأسماء", "لوجوهات الشعارات", "مراجعة اللوجو الذي يرفعه المالك لشعاره واعتماده للظهور في سجل الأسماء أو رفضه"),
        new(NamesLatin, "سجل الأسماء", "الكتابة اللاتينية للأسماء", "تصحيح كتابة الأسماء بالحروف اللاتينية كما تظهر في الموقع الإنجليزي والشهادات"),
        new(ViolationsView, "المخالفات", "عرض المخالفات", "قائمة المخالفات والغرامات الصادرة للملاك والبحث فيها وتنزيل مرفقاتها"),
        new(ViolationsManage, "المخالفات", "إصدار المخالفات وإلغاؤها", "إصدار مخالفة لمالك من أنواع المخالفات المعتمدة مع الوصف والمرفق، وإلغاء مخالفة غير مسددة مع ذكر السبب"),
        new(ReportsMarks, "التقارير", "تقرير الشعارات", "كل المزادات برقمها: الطلب والمالك والفائز وقيمة الفوز والفاتورة، مع الطباعة والتصدير"),
        new(TicketsView, "الدعم", "عرض الشكاوى والبلاغات", "قائمة التذاكر وتفاصيلها"),
        new(TicketsReply, "الدعم", "الرد على التذاكر", "الرد على العميل وكتابة ملاحظات داخلية"),
        new(TicketsManage, "الدعم", "إدارة التذاكر", "تغيير الحالة والأولوية والإسناد"),
        new(EventsManage, "المحتوى", "المواعيد والفعاليات", "إضافة فعاليات الإبل وتعديلها وحذفها"),
        new(OwnersView, "العملاء", "بيانات العملاء (CRM)", "قائمة الملاك وشعاراتهم حسب الفئة؛ الهوية والجوال يظهران مخفيين جزئيًا"),
        new(OwnersFull, "العملاء", "البيانات الكاملة والطباعة والتصدير", "إظهار رقم الهوية والجوال كاملين، وطباعة التقرير وتصديره (يُسجَّل في سجل التغييرات)"),
        new(UsersView, "المستخدمون", "عرض المستخدمين", "قائمة الملاك والموظفين"),
        new(UsersCreate, "المستخدمون", "إنشاء مستخدم", "إضافة حسابات موظفين جديدة"),
        new(UsersRoles, "المستخدمون", "تحديد دور الموظف", "تغيير دور الموظف وصلاحياته"),
        new(UsersSuspend, "المستخدمون", "إيقاف الحسابات وتفعيلها", "إيقاف حساب مالك أو موظف وإعادة تفعيله"),
        new(RolesManage, "المستخدمون", "الأدوار والصلاحيات", "إنشاء الأدوار وتحديد صلاحيات كل دور"),
        new(SettingsManage, "الإعدادات", "إعدادات المنصة", "استقبال الطلبات، الإعلان، الصيانة، بيانات التواصل"),
        new(SecurityManage, "الإعدادات", "سياسة الأمان", "كلمات المرور وقفل الدخول ومهلة الخمول وتسجيل خروج الجميع"),
        new(AuditView, "الإعدادات", "سجل التغييرات", "سجل تعديلات الإعدادات والإجراءات الإدارية"),
        new(LogsView, "الإعدادات", "سجل النشاط (Logs)", "كل ما يجري على المنصة بالتاريخ والوقت: الدخول والطلبات والمزايدات والمدفوعات وإجراءات الموظفين والمفوضين"),
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
