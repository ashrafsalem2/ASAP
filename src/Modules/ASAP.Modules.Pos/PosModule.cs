using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Pos;

/// <summary>
/// The point of sale module: tills, sessions and receipts.
/// </summary>
public sealed class PosModule : IAsapModule, ASAP.Platform.Kernel.Sync.ISyncContributor
{
    /// <summary>The module identifier used in every point of sale permission and setting key.</summary>
    public const string Id = "Pos";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Point of sale", "نقاط البيع");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Tills, cashier sessions and receipts, with the drawer counted at the end of every turn "
        + "and any difference posted rather than argued about.",
        "نقاط البيع وورديات الكاشير والإيصالات، مع جرد الدرج في نهاية كل وردية وترحيل أي فارق "
        + "بدلاً من الجدل حوله.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// Point of sale sits above Sales, and through it above Inventory and Finance.
    /// </summary>
    /// <remarks>
    /// A receipt is a sales invoice with a cash drawer attached, and it posts through the same
    /// machinery for the same reason: the P&amp;L must not be able to tell which door a sale came
    /// through. See docs/architecture/module-dependencies.md.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn =>
    [
        Platform.Core.Modules.PlatformModule.Id,
        Modules.Inventory.InventoryModule.Id,
        Modules.Finance.FinanceModule.Id,
        Modules.Sales.SalesModule.Id,
        Modules.Promotions.PromotionsModule.Id,
    ];

    /// <summary>
    /// Which tills exist is head office's business; what they took is theirs.
    /// </summary>
    /// <remarks>
    /// The direction of a receipt is the clearest case in the system. It records money that
    /// changed hands in a shop, and head office has no standing to restate it.
    /// </remarks>
    public IReadOnlyCollection<ASAP.Platform.Kernel.Sync.SyncEntityDescriptor> SyncEntities =>
    [
        new("Pos.Station", typeof(Stations.PosStation), ASAP.Platform.Kernel.Sync.SyncDirection.Down, Id),
        new("Pos.Session", typeof(Sessions.PosSession), ASAP.Platform.Kernel.Sync.SyncDirection.Up, Id),
        new("Pos.Receipt", typeof(Receipts.PosReceipt), ASAP.Platform.Kernel.Sync.SyncDirection.Up, Id),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => PosMessages.All;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<Stations.StationBranchLookup>();
        services.AddScoped<Seed.PosSeeder>();
        services.AddScoped<Sessions.PosSessionService>();
        services.AddScoped<Receipts.PosReceiptService>();
        services.AddScoped<Reporting.PromotionUptakeReport>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Station", PermissionAction.Read,
            new LocalizedText("View tills", "عرض نقاط البيع")),

        PermissionDescriptor.Define(
            Id, "Station", PermissionAction.Update,
            new LocalizedText("Maintain tills", "إدارة نقاط البيع"),
            implies: [$"{Id}.Station.Read"]),

        PermissionDescriptor.Define(
            Id, "Session", PermissionAction.Read,
            new LocalizedText("View till sessions", "عرض ورديات نقاط البيع")),

        PermissionDescriptor.Define(
            Id, "Session", PermissionAction.Create,
            new LocalizedText("Open a till", "فتح وردية"),
            new LocalizedText(
                "Start a session with an opening float. The float is what the drawer is counted "
                + "against at the end, so it is recorded rather than assumed.",
                "بدء وردية بعهدة افتتاحية. والعهدة هي ما يُجرد الدرج مقابله في النهاية، لذا "
                + "تُسجَّل ولا تُفترض."),
            implies: [$"{Id}.Session.Read"]),

        PermissionDescriptor.Define(
            Id, "Session", PermissionAction.Post,
            new LocalizedText("Close a till and declare the cash", "إغلاق الوردية وجرد النقد"),
            new LocalizedText(
                "Count the drawer and finish the session. Any difference between the count and "
                + "what was taken is posted, which is what keeps the cash account describing the "
                + "money that is actually there.",
                "جرد الدرج وإنهاء الوردية. ويُرحّل أي فارق بين الجرد وما تم استلامه، وهو ما يبقي "
                + "حساب النقدية معبّرًا عن المبلغ الموجود فعلاً."),
            implies: [$"{Id}.Session.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Session", PermissionAction.Override,
            new LocalizedText("Close a till with sales still parked", "إغلاق الوردية مع وجود عمليات معلّقة"),
            new LocalizedText(
                "Finish a session that still has unpaid sales set aside on it. Every use is audited.",
                "إنهاء وردية ما زالت عليها عمليات معلّقة غير مدفوعة. ويتم تدقيق كل استخدام."),
            implies: [$"{Id}.Session.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Receipt", PermissionAction.Read,
            new LocalizedText("View receipts", "عرض الإيصالات")),

        PermissionDescriptor.Define(
            Id, "Receipt", PermissionAction.Post,
            new LocalizedText("Take payment", "استيفاء الدفع"),
            new LocalizedText(
                "Ring up a sale and take the money for it. This moves stock and posts revenue, "
                + "tax and the tender in one transaction.",
                "تسجيل عملية بيع واستيفاء قيمتها. يحرّك هذا المخزون ويرحّل الإيراد والضريبة "
                + "ووسيلة الدفع في حركة واحدة."),
            implies: [$"{Id}.Receipt.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Receipt", PermissionAction.Override,
            new LocalizedText("Approve a discount above the till limit", "اعتماد خصم يتجاوز حد النقطة"),
            new LocalizedText(
                "Allow a discount larger than a cashier may give unasked. Every use is audited "
                + "against the name of whoever approved it.",
                "السماح بخصم يتجاوز ما يمكن للكاشير منحه دون موافقة. ويتم تدقيق كل استخدام باسم "
                + "من اعتمده."),
            implies: [$"{Id}.Receipt.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run till reports", "تشغيل تقارير نقاط البيع")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Posting.CashAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Cash in drawer", "النقد في الدرج"),
            Description = new LocalizedText(
                "Where cash taken at a till is held until it is banked. Distinct from the bank "
                + "account on purpose: money in a drawer and money at the bank are not the same "
                + "asset, and a company that treats them as one cannot tell you what it has.",
                "الحساب الذي يُحتفظ فيه بالنقد المستلم في نقاط البيع حتى إيداعه. وهو منفصل عن "
                + "حساب البنك عمدًا، فالنقد في الدرج والنقد في البنك ليسا أصلاً واحدًا، والشركة "
                + "التي تخلط بينهما لا تعرف ما لديها."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "1100",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.CardAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Card clearing", "تسوية البطاقات"),
            Description = new LocalizedText(
                "Where card takings sit between the sale and the day the acquirer pays. Money "
                + "the company has earned and does not yet hold, which is a real thing and worth "
                + "seeing on its own line.",
                "الحساب الذي تُقيّد فيه مبالغ البطاقات بين تاريخ البيع ويوم تحصيلها من مزوّد "
                + "الخدمة. وهي مبالغ استحقتها الشركة ولم تقبضها بعد، وهذا واقع يستحق سطرًا خاصًا."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "1150",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.VoucherAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Vouchers redeemed", "القسائم المستردة"),
            Description = new LocalizedText(
                "Where a gift card or credit note is charged when it is spent. It is a liability "
                + "the company already owed, being discharged.",
                "الحساب الذي تُخصم منه قيمة القسيمة أو الإشعار الدائن عند استخدامه، فهو التزام "
                + "قائم على الشركة يجري سداده."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "2350",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.VarianceAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Till differences", "فروقات نقاط البيع"),
            Description = new LocalizedText(
                "Where the difference goes when a drawer counts to something other than it should. "
                + "Posting it is not an accusation, it is what makes the cash account describe the "
                + "money in the building.",
                "الحساب الذي يُرحّل إليه الفارق حين يختلف جرد الدرج عن المتوقع. وترحيله ليس اتهامًا، "
                + "بل هو ما يجعل حساب النقدية معبّرًا عن المبلغ الموجود فعلاً."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "6910",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.RoundingAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Cash rounding", "تقريب النقد"),
            Description = new LocalizedText(
                "Where the halalas go that were rounded off a cash total. Small, constant, and "
                + "the reason a till that does not have this account never quite balances.",
                "الحساب الذي تُرحّل إليه الهللات الناتجة عن تقريب إجمالي النقد. مبالغ صغيرة "
                + "ومستمرة، وغيابها هو سبب عدم توازن نقطة البيع باستمرار."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "6920",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Cash.RoundingIncrement",
            Module = Id,
            Group = new LocalizedText("Cash", "النقد"),
            DisplayName = new LocalizedText("Round cash totals to", "تقريب إجمالي النقد إلى"),
            Description = new LocalizedText(
                "The smallest coin a customer can actually pay with. A total of 12.03 cannot be "
                + "settled in a country whose smallest coin is five halalas, so it is rounded to "
                + "12.05 and the difference posted. Zero switches rounding off.",
                "أصغر فئة نقدية يمكن للعميل الدفع بها. فإجمالي 12.03 لا يمكن سداده في بلد أصغر "
                + "عملاته خمس هللات، فيُقرَّب إلى 12.05 ويُرحّل الفارق. والقيمة صفر تعطّل التقريب."),
            ValueType = SetupValueType.Decimal,
            Scope = SetupScope.Company,
            DefaultValue = "0.05",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Receipts.DiscountLimitPercent",
            Module = Id,
            Group = new LocalizedText("Selling", "البيع"),
            DisplayName = new LocalizedText("Discount a cashier may give", "الخصم المسموح للكاشير"),
            Description = new LocalizedText(
                "How much a cashier may take off without asking anybody. Above this the till "
                + "refuses and names the permission that can approve it, so the queue waits for a "
                + "supervisor rather than for an argument.",
                "نسبة الخصم التي يمكن للكاشير منحها دون الرجوع لأحد. وما فوقها ترفضه النقطة "
                + "وتذكر الصلاحية التي تعتمده، فينتظر الطابور مشرفًا لا نقاشًا."),
            ValueType = SetupValueType.Decimal,
            Scope = SetupScope.Company,
            DefaultValue = "10",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Sessions.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Session numbers", "ترقيم الورديات"),
            Description = new LocalizedText(
                "The series till session numbers are issued from.",
                "المسلسل الذي تصدر منه أرقام الورديات."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "POS-SESS",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
        new()
        {
            Key = $"{Id}.Receipts.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Receipt numbers", "ترقيم الإيصالات"),
            Description = new LocalizedText(
                "The series receipt numbers are issued from. Use a gapless series: a till receipt "
                + "is a simplified tax invoice, and a sequence with holes in it is a question from "
                + "the authority.",
                "المسلسل الذي تصدر منه أرقام الإيصالات. استخدم مسلسلاً بلا فجوات، فإيصال نقطة "
                + "البيع فاتورة ضريبية مبسطة، وتسلسلها المتقطع يستدعي استفسارًا من الهيئة."),
            ValueType = SetupValueType.Text,

            // Branch rather than company, because receipts are usually numbered per shop: two
            // tills selling at once must not collide, and a receipt number that says on its face
            // where it was issued is worth a great deal when somebody brings one back.
            Scope = SetupScope.Branch,
            DefaultValue = "POS-RCP",
            RequiresPermission = $"{Id}.Station.Update",
            HelpTopic = "pos/setup",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Pos.Root",
            Module = Id,
            DisplayName = new LocalizedText("Point of sale", "نقاط البيع"),
            Kind = NavigationKind.Group,
            Icon = "pos",
            Order = 500,
        },
        new()
        {
            Id = "Pos.Till",
            Module = Id,
            ParentId = "Pos.Root",
            DisplayName = new LocalizedText("Till", "نقطة البيع"),
            Kind = NavigationKind.Page,
            Route = "/pos/till",
            RequiresPermission = $"{Id}.Receipt.Read",
            Order = 10,
        },
        new()
        {
            Id = "Pos.Sessions",
            Module = Id,
            ParentId = "Pos.Root",
            DisplayName = new LocalizedText("Sessions", "الورديات"),
            Kind = NavigationKind.Page,
            Route = "/pos/sessions",
            RequiresPermission = $"{Id}.Session.Read",
            Order = 20,
        },
        new()
        {
            Id = "Pos.Promotions",
            Module = Id,
            ParentId = "Pos.Root",
            DisplayName = new LocalizedText("What the offers did", "أثر العروض"),
            Kind = NavigationKind.Report,
            Route = "/pos/promotions",
            RequiresPermission = $"{Id}.Report.Read",
            Order = 25,
        },
        new()
        {
            Id = "Pos.Promotions",
            Module = Id,
            ParentId = "Pos.Root",
            DisplayName = new LocalizedText("What the offers did", "أثر العروض"),
            Kind = NavigationKind.Report,
            Route = "/pos/promotions",
            RequiresPermission = $"{Id}.Report.Read",
            Order = 25,
        },
        new()
        {
            Id = "Pos.Stations",
            Module = Id,
            ParentId = "Pos.Root",
            DisplayName = new LocalizedText("Tills", "أجهزة نقاط البيع"),
            Kind = NavigationKind.Setup,
            Route = "/pos/stations",
            RequiresPermission = $"{Id}.Station.Read",
            Order = 30,
        },
    ];
}
