using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Platform.Core.Modules;

/// <summary>
/// The platform presented as a module, so administration is described the same way every other
/// part of ASAP is.
/// </summary>
/// <remarks>
/// Registering the platform through the same interface modules use is not a formality. It means
/// the permission screen, the setup screen and the menu are assembled from one uniform source,
/// and it means the module mechanism is exercised by ASAP itself rather than only by other people
/// code. It carries no licence feature, because administration is not something a customer can
/// decline to buy.
/// </remarks>
public sealed class PlatformModule : IAsapModule
{
    /// <summary>The module identifier used in every platform permission and setting key.</summary>
    public const string Id = "Platform";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Administration", "الإدارة");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Companies, branches, users, permissions, dimensions, number series and system setup.",
        "الشركات والفروع والمستخدمون والصلاحيات والأبعاد والمسلسلات وإعدادات النظام.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string? LicenseFeature => null;

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => [];

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The platform registers its own services in AddAsapCore, before modules are asked.
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        .. Crud("Company", new LocalizedText("companies", "الشركات"), sensitiveWrite: true),
        .. Crud("Branch", new LocalizedText("branches", "الفروع")),
        .. Crud("User", new LocalizedText("user accounts", "حسابات المستخدمين"), sensitiveWrite: true),
        .. Crud("PermissionSet", new LocalizedText("permission sets", "مجموعات الصلاحيات"), sensitiveWrite: true),
        .. Crud("Dimension", new LocalizedText("dimensions", "الأبعاد")),
        .. Crud("NumberSeries", new LocalizedText("number series", "المسلسلات")),

        PermissionDescriptor.Define(
            Id, "Setup", PermissionAction.Read,
            new LocalizedText("View system setup", "عرض إعدادات النظام")),

        PermissionDescriptor.Define(
            Id, "Setup", PermissionAction.Update,
            new LocalizedText("Change system setup", "تغيير إعدادات النظام"),
            new LocalizedText(
                "Change any setting the module screens expose. Some settings govern how figures "
                + "are calculated, so this carries real weight.",
                "تغيير أي إعداد تعرضه شاشات الوحدات. بعض الإعدادات تحكم طريقة احتساب الأرقام، "
                + "لذا فإن هذه الصلاحية ذات أثر كبير."),
            implies: [$"{Id}.Setup.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "NumberSeries", PermissionAction.Override,
            new LocalizedText("Override number series date order", "تجاوز التسلسل الزمني للمسلسل"),
            new LocalizedText(
                "Post a document dated earlier than the last one the series numbered.",
                "ترحيل مستند بتاريخ أسبق من آخر مستند رقّمه المسلسل."),
            implies: [$"{Id}.NumberSeries.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Sync", PermissionAction.Read,
            new LocalizedText("View branch synchronisation", "عرض مزامنة الفروع"),
            new LocalizedText(
                "See which branches are behind and by how much, without telephoning them.",
                "معرفة الفروع المتأخرة ومقدار تأخرها دون الاتصال بها.")),

        PermissionDescriptor.Define(
            Id, "Sync", PermissionAction.Execute,
            new LocalizedText("Synchronise a branch", "مزامنة فرع"),
            new LocalizedText(
                "Pull master data down to a branch and push its documents up. Held by the branch "
                + "itself rather than by a person: it is what a shop signs in as to keep working "
                + "when the line comes back.",
                "سحب البيانات الأساسية إلى الفرع ودفع مستنداته إلى المركز. وهي صلاحية للفرع نفسه "
                + "لا لشخص، فهي ما يستخدمه المتجر لمواصلة العمل عند عودة الاتصال."),
            implies: [$"{Id}.Sync.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "AuditLog", PermissionAction.Read,
            new LocalizedText("View the audit log", "عرض سجل التدقيق"),
            new LocalizedText(
                "See who did what, including every time someone overrode a protection.",
                "معرفة من قام بماذا، بما في ذلك كل حالة تجاوز لأحد القيود.")),

        PermissionDescriptor.Define(
            Id, "AuditLog", PermissionAction.Export,
            new LocalizedText("Export the audit log", "تصدير سجل التدقيق"),
            implies: [$"{Id}.AuditLog.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Extension", PermissionAction.Read,
            new LocalizedText("View installed extensions", "عرض الإضافات المثبتة")),

        PermissionDescriptor.Define(
            Id, "Extension", PermissionAction.Execute,
            new LocalizedText("Install and remove extensions", "تثبيت وإزالة الإضافات"),
            new LocalizedText(
                "An extension runs with full access to every company books. Grant this to "
                + "almost nobody.",
                "تعمل الإضافة بصلاحية كاملة على دفاتر جميع الشركات. امنح هذه الصلاحية لأضيق نطاق ممكن."),
            implies: [$"{Id}.Extension.Read"],
            isSensitive: true),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Security.MaxFailedLoginAttempts",
            Module = Id,
            Group = new LocalizedText("Security", "الأمان"),
            DisplayName = new LocalizedText("Failed sign-ins before lockout", "عدد المحاولات الفاشلة قبل الإيقاف"),
            Description = new LocalizedText(
                "How many wrong passwords in a row lock an account. Set to 0 to never lock.",
                "عدد كلمات المرور الخاطئة المتتالية التي توقف الحساب. اضبطه على 0 لتعطيل الإيقاف."),
            ValueType = SetupValueType.Integer,
            Scope = SetupScope.Tenant,
            DefaultValue = "5",
            Minimum = 0,
            Maximum = 50,
            RequiresPermission = $"{Id}.Setup.Update",
        },
        new()
        {
            Key = $"{Id}.Security.LockoutMinutes",
            Module = Id,
            Group = new LocalizedText("Security", "الأمان"),
            DisplayName = new LocalizedText("Lockout duration in minutes", "مدة الإيقاف بالدقائق"),
            Description = new LocalizedText(
                "How long an account stays locked after too many failed sign-ins.",
                "المدة التي يبقى فيها الحساب موقوفًا بعد تجاوز عدد المحاولات الفاشلة."),
            ValueType = SetupValueType.Integer,
            Scope = SetupScope.Tenant,
            DefaultValue = "15",
            Minimum = 1,
            Maximum = 1440,
            RequiresPermission = $"{Id}.Setup.Update",
        },
        new()
        {
            Key = $"{Id}.Audit.RetentionDays",
            Module = Id,
            Group = new LocalizedText("Audit", "التدقيق"),
            DisplayName = new LocalizedText("Keep audit entries for (days)", "مدة الاحتفاظ بسجل التدقيق (بالأيام)"),
            Description = new LocalizedText(
                "How long ordinary audit entries are kept. Overrides and permission changes are "
                + "kept regardless of this setting.",
                "مدة الاحتفاظ بسجلات التدقيق العادية. يتم الاحتفاظ بحالات التجاوز وتغييرات "
                + "الصلاحيات بغض النظر عن هذا الإعداد."),
            ValueType = SetupValueType.Integer,
            Scope = SetupScope.Tenant,
            DefaultValue = "3650",
            Minimum = 90,
            RequiresPermission = $"{Id}.Setup.Update",
        },
        new()
        {
            Key = $"{Id}.Ui.DefaultCulture",
            Module = Id,
            Group = new LocalizedText("Appearance", "المظهر"),
            DisplayName = new LocalizedText("Default language", "اللغة الافتراضية"),
            Description = new LocalizedText(
                "The language new users start in. Each user can change their own.",
                "اللغة التي يبدأ بها المستخدمون الجدد. يمكن لكل مستخدم تغييرها."),
            ValueType = SetupValueType.Option,
            Scope = SetupScope.Tenant,
            DefaultValue = "en",
            AllowedValues =
            [
                new SetupOption("en", new LocalizedText("English", "الإنجليزية")),
                new SetupOption("ar", new LocalizedText("Arabic", "العربية")),
            ],
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Platform.Administration",
            Module = Id,
            DisplayName = new LocalizedText("Administration", "الإدارة"),
            Kind = NavigationKind.Group,
            Icon = "settings",
            Order = 900,
        },
        Page("Companies", new LocalizedText("Companies", "الشركات"), "/admin/companies", "Company", 10),
        Page("Branches", new LocalizedText("Branches", "الفروع"), "/admin/branches", "Branch", 20),
        Page("Users", new LocalizedText("Users", "المستخدمون"), "/admin/users", "User", 30),
        Page(
            "PermissionSets",
            new LocalizedText("Permission sets", "مجموعات الصلاحيات"),
            "/admin/permission-sets",
            "PermissionSet",
            40),
        Page("Dimensions", new LocalizedText("Dimensions", "الأبعاد"), "/admin/dimensions", "Dimension", 50),
        Page(
            "NumberSeries",
            new LocalizedText("Number series", "المسلسلات"),
            "/admin/number-series",
            "NumberSeries",
            60),
        new()
        {
            Id = "Platform.Setup",
            Module = Id,
            ParentId = "Platform.Administration",
            DisplayName = new LocalizedText("System setup", "إعدادات النظام"),
            Kind = NavigationKind.Setup,
            Route = "/admin/setup",
            Icon = "tune",
            RequiresPermission = $"{Id}.Setup.Read",
            Order = 70,
        },
        new()
        {
            Id = "Platform.ChangePassword",
            Module = Id,
            ParentId = "Platform.Administration",
            DisplayName = new LocalizedText("Your password", "كلمة المرور"),
            Route = "/account/password",
            Icon = "key",

            // No permission. Changing your own password is the one thing every account must be
            // able to do, and an account created by an administrator holds a password somebody
            // else chose until it is used.
            Order = 75,
        },
        new()
        {
            Id = "Platform.AuditLog",
            Module = Id,
            ParentId = "Platform.Administration",
            DisplayName = new LocalizedText("Audit log", "سجل التدقيق"),
            Kind = NavigationKind.Report,
            Route = "/admin/audit-log",
            Icon = "history",
            RequiresPermission = $"{Id}.AuditLog.Read",
            Order = 80,
        },
        new()
        {
            Id = "Platform.Help",
            Module = Id,
            ParentId = "Platform.Administration",
            DisplayName = new LocalizedText("Help", "المساعدة"),
            Kind = NavigationKind.Page,
            Route = "/help",
            Icon = "help",

            // No permission. Every refusal in the system links here, and a link that refuses the
            // person following it is worse than no link.
            Order = 95,
        },
        new()
        {
            Id = "Platform.Reference",
            Module = Id,
            ParentId = "Platform.Administration",
            DisplayName = new LocalizedText("Developer reference", "المرجع التقني"),
            Kind = NavigationKind.Report,
            Route = "/admin/reference",
            Icon = "code",

            // No permission. It describes what the installation declares, which is the same
            // information the menu, the setup screen and every refusal already disclose.
            Order = 96,
        },
        // No entry for extensions yet. The screen is Phase 9 work and the endpoint does not
        // exist, and a menu that advertises a feature which is not there is worse than a menu
        // that stays quiet about it -- somebody clicks it, is bounced home, and stops trusting
        // the rest of the menu. It comes back when there is something to click through to.
    ];

    private static NavigationItem Page(
        string name,
        LocalizedText displayName,
        string route,
        string resource,
        int order)
        => new()
        {
            Id = $"{Id}.{name}",
            Module = Id,
            ParentId = "Platform.Administration",
            DisplayName = displayName,
            Route = route,
            RequiresPermission = PermissionDescriptor.BuildKey(Id, resource, PermissionAction.Read),
            Order = order,
        };

    /// <summary>
    /// Declares the four ordinary verbs over a resource, with each write implying the read.
    /// </summary>
    /// <remarks>
    /// Most administration resources need exactly this shape, and writing it out four times per
    /// resource invites the mistake of granting an update without the matching read.
    /// </remarks>
    private static IEnumerable<PermissionDescriptor> Crud(
        string resource,
        LocalizedText plural,
        bool sensitiveWrite = false)
    {
        var read = PermissionDescriptor.BuildKey(Id, resource, PermissionAction.Read);

        yield return PermissionDescriptor.Define(
            Id, resource, PermissionAction.Read,
            new LocalizedText($"View {plural.English}", $"عرض {plural.Arabic}"));

        yield return PermissionDescriptor.Define(
            Id, resource, PermissionAction.Create,
            new LocalizedText($"Add {plural.English}", $"إضافة {plural.Arabic}"),
            implies: [read],
            isSensitive: sensitiveWrite);

        yield return PermissionDescriptor.Define(
            Id, resource, PermissionAction.Update,
            new LocalizedText($"Change {plural.English}", $"تعديل {plural.Arabic}"),
            implies: [read],
            isSensitive: sensitiveWrite);

        yield return PermissionDescriptor.Define(
            Id, resource, PermissionAction.Delete,
            new LocalizedText($"Remove {plural.English}", $"حذف {plural.Arabic}"),
            implies: [read],
            isSensitive: true);
    }
}
