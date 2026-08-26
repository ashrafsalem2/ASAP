using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Finance;

/// <summary>
/// The Finance module: chart of accounts, fiscal periods, journals, the general ledger and the
/// posting engine every other module writes through.
/// </summary>
public sealed class FinanceModule : IAsapModule
{
    /// <summary>The module identifier used in every Finance permission and setting key.</summary>
    public const string Id = "Finance";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Finance", "المالية");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Chart of accounts, fiscal periods, general journals, the general ledger and financial reporting.",
        "شجرة الحسابات والفترات المالية وقيود اليومية ودفتر الأستاذ العام والتقارير المالية.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// Finance loads after the platform, whose dimensions and number series it posts through.
    /// </summary>
    /// <remarks>
    /// Stated rather than assumed. Without it the two modules are unrelated in the graph and the
    /// order falls to the alphabetical tie-break, which happens to put Finance first -- correct
    /// today, and quietly wrong the moment Finance seeding needs a platform number series to exist.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn => [Platform.Core.Modules.PlatformModule.Id];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => FinanceMessages.All;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<JournalPostingValidator>();
        services.AddScoped<JournalPostingService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Account", PermissionAction.Read,
            new LocalizedText("View the chart of accounts", "عرض شجرة الحسابات")),

        PermissionDescriptor.Define(
            Id, "Account", PermissionAction.Create,
            new LocalizedText("Add accounts", "إضافة حسابات"),
            implies: [$"{Id}.Account.Read"]),

        PermissionDescriptor.Define(
            Id, "Account", PermissionAction.Update,
            new LocalizedText("Change accounts", "تعديل حسابات"),
            implies: [$"{Id}.Account.Read"]),

        PermissionDescriptor.Define(
            Id, "Account", PermissionAction.Override,
            new LocalizedText("Post directly to a system account", "الترحيل المباشر إلى حساب النظام"),
            new LocalizedText(
                "Post by hand to a control account such as receivables, which is normally written "
                + "only by the module that owns it. Every use is audited.",
                "الترحيل يدويًا إلى حساب مراقبة مثل حسابات العملاء، وهو حساب تكتبه عادة الوحدة "
                + "المالكة له فقط. ويتم تدقيق كل استخدام."),
            implies: [$"{Id}.Account.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Journal", PermissionAction.Read,
            new LocalizedText("View journals", "عرض قيود اليومية")),

        PermissionDescriptor.Define(
            Id, "Journal", PermissionAction.Create,
            new LocalizedText("Prepare journals", "إعداد قيود اليومية"),
            implies: [$"{Id}.Journal.Read"]),

        PermissionDescriptor.Define(
            Id, "Journal", PermissionAction.Post,
            new LocalizedText("Post journals", "ترحيل قيود اليومية"),
            new LocalizedText(
                "Commit a journal to the ledger. Deliberately separate from preparing one: the "
                + "clerk who keys a journal is usually not the person who approves it.",
                "ترحيل القيد إلى دفتر الأستاذ. وهي منفصلة عمدًا عن الإعداد: فالموظف الذي يُدخل "
                + "القيد ليس عادةً من يعتمده."),
            implies: [$"{Id}.Journal.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Entry", PermissionAction.Read,
            new LocalizedText("View ledger entries", "عرض قيود دفتر الأستاذ")),

        PermissionDescriptor.Define(
            Id, "Entry", PermissionAction.Reverse,
            new LocalizedText("Reverse a posting", "عكس قيد مرحّل"),
            new LocalizedText(
                "Post a reversal of an existing transaction. The original stays visible; nothing "
                + "is ever deleted.",
                "ترحيل عكس لحركة قائمة. يبقى القيد الأصلي ظاهرًا، ولا يُحذف أي شيء أبدًا."),
            implies: [$"{Id}.Entry.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Period", PermissionAction.Read,
            new LocalizedText("View fiscal periods", "عرض الفترات المالية")),

        PermissionDescriptor.Define(
            Id, "Period", PermissionAction.Update,
            new LocalizedText("Open and close periods", "فتح وإغلاق الفترات"),
            implies: [$"{Id}.Period.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Period", PermissionAction.Override,
            new LocalizedText("Post into a closed period", "الترحيل إلى فترة مغلقة"),
            new LocalizedText(
                "Post to a period that has been closed. Does not extend to a closed financial "
                + "year, which nobody may post into. Every use is audited.",
                "الترحيل إلى فترة تم إغلاقها. ولا يشمل ذلك السنة المالية المغلقة، التي لا يمكن "
                + "لأحد الترحيل إليها. ويتم تدقيق كل استخدام."),
            implies: [$"{Id}.Period.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run financial reports", "تشغيل التقارير المالية")),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Export,
            new LocalizedText("Export financial reports", "تصدير التقارير المالية"),
            implies: [$"{Id}.Report.Read"],
            isSensitive: true),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Posting.AllowFrom",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Allow posting from", "السماح بالترحيل من"),
            Description = new LocalizedText(
                "Earliest date that may be posted to. Set at company level as policy, and narrowed "
                + "per user to hold a clerk to the current month while the controller finishes the close.",
                "أقرب تاريخ يُسمح بالترحيل إليه. يُضبط على مستوى الشركة كسياسة عامة، ويُضيّق لكل "
                + "مستخدم لإبقاء الموظف ضمن الشهر الحالي بينما يُكمل المدير المالي الإقفال."),
            ValueType = SetupValueType.Date,
            Scope = SetupScope.Company,
            RequiresPermission = $"{Id}.Period.Update",
            HelpTopic = "finance/posting-window",
        },
        new()
        {
            Key = $"{Id}.Posting.AllowTo",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Allow posting to", "السماح بالترحيل حتى"),
            Description = new LocalizedText(
                "Latest date that may be posted to. Stops a mistyped year putting an entry in 2036.",
                "آخر تاريخ يُسمح بالترحيل إليه. يمنع خطأً مطبعيًا في السنة من وضع قيد في 2036."),
            ValueType = SetupValueType.Date,
            Scope = SetupScope.Company,
            RequiresPermission = $"{Id}.Period.Update",
            HelpTopic = "finance/posting-window",
        },
        new()
        {
            Key = $"{Id}.General.RetainedEarningsAccount",
            Module = Id,
            Group = new LocalizedText("Year end", "نهاية السنة"),
            DisplayName = new LocalizedText("Retained earnings account", "حساب الأرباح المبقاة"),
            Description = new LocalizedText(
                "Where the year's result is transferred at year end, so the new year opens with the "
                + "income statement at zero.",
                "الحساب الذي تُرحّل إليه نتيجة السنة عند الإقفال، بحيث تبدأ السنة الجديدة وقائمة "
                + "الدخل عند الصفر."),
            ValueType = SetupValueType.EntityReference,
            ReferencedEntityType = "Finance.GlAccount",
            Scope = SetupScope.Company,
            RequiresPermission = $"{Id}.Period.Update",
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Key = $"{Id}.General.RequireDescription",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Require a description on every line", "إلزام وصف لكل سطر"),
            Description = new LocalizedText(
                "When on, a journal line must carry its own description rather than inheriting the "
                + "account name. Worth turning on where ledger enquiries are read by people who did "
                + "not key the entries.",
                "عند التفعيل، يجب أن يحمل كل سطر وصفًا خاصًا به بدلاً من وراثة اسم الحساب. يُفضّل "
                + "تفعيله عندما يطّلع على القيود أشخاص لم يُدخلوها."),
            ValueType = SetupValueType.Boolean,
            Scope = SetupScope.Company,
            DefaultValue = "false",
            RequiresPermission = $"{Id}.Period.Update",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Finance.Root",
            Module = Id,
            DisplayName = new LocalizedText("Finance", "المالية"),
            Kind = NavigationKind.Group,
            Icon = "account_balance",
            Order = 100,
        },
        Page("ChartOfAccounts", new LocalizedText("Chart of accounts", "شجرة الحسابات"),
            "/finance/accounts", $"{Id}.Account.Read", 10),
        Page("Journals", new LocalizedText("General journals", "قيود اليومية"),
            "/finance/journals", $"{Id}.Journal.Read", 20),
        Page("Entries", new LocalizedText("Ledger entries", "قيود دفتر الأستاذ"),
            "/finance/entries", $"{Id}.Entry.Read", 30),
        new()
        {
            Id = "Finance.Periods",
            Module = Id,
            ParentId = "Finance.Root",
            DisplayName = new LocalizedText("Fiscal periods", "الفترات المالية"),
            Kind = NavigationKind.Setup,
            Route = "/finance/periods",
            RequiresPermission = $"{Id}.Period.Read",
            Order = 40,
        },
        new()
        {
            Id = "Finance.TrialBalance",
            Module = Id,
            ParentId = "Finance.Root",
            DisplayName = new LocalizedText("Trial balance", "ميزان المراجعة"),
            Kind = NavigationKind.Report,
            Route = "/finance/reports/trial-balance",
            RequiresPermission = $"{Id}.Report.Read",
            Order = 50,
        },
    ];

    private static NavigationItem Page(
        string name,
        LocalizedText displayName,
        string route,
        string permission,
        int order)
        => new()
        {
            Id = $"{Id}.{name}",
            Module = Id,
            ParentId = "Finance.Root",
            DisplayName = displayName,
            Route = route,
            RequiresPermission = permission,
            Order = order,
        };
}
