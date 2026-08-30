using ASAP.Modules.Inventory.Costing;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Inventory;

/// <summary>
/// The Inventory module: items, locations, stock movements, costing and transfers.
/// </summary>
public sealed class InventoryModule : IAsapModule, ASAP.Platform.Kernel.Sync.ISyncContributor
{
    /// <summary>The module identifier used in every Inventory permission and setting key.</summary>
    public const string Id = "Inventory";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Inventory", "المخزون");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Items, locations, stock movements, costing and transfers between branches.",
        "الأصناف والمواقع وحركات المخزون والتكلفة والتحويلات بين الفروع.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// Inventory posts the value of every movement into the general ledger, so Finance has to be
    /// there first.
    /// </summary>
    /// <remarks>
    /// Finance is named as a string rather than as <c>FinanceModule.Id</c>, and that is not
    /// laziness. Inventory holds no reference to the Finance assembly and must not: a module that
    /// referenced another could not be sold without it, which is the whole point of the
    /// architecture. Dependencies are declared by identifier so the host can resolve them without
    /// either module knowing the other's types, and the compiler enforces the rule by refusing to
    /// see across the boundary at all.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn => [Platform.Core.Modules.PlatformModule.Id, "Finance"];

    /// <summary>
    /// The catalogue and the map of where stock lives travel down; what moved travels up.
    /// </summary>
    /// <remarks>
    /// A shop reads the item list and never writes it, and writes its own movements and never
    /// reads another shop's. That is the whole conflict story for this module. See
    /// docs/architecture/branch-synchronisation.md.
    /// </remarks>
    public IReadOnlyCollection<ASAP.Platform.Kernel.Sync.SyncEntityDescriptor> SyncEntities =>
    [
        // Quantity on hand is the company total and moves on every movement; a shop knows its
        // own. The costs stay published, because a till warns on selling below them.
        new("Inventory.Item", typeof(Items.Item), ASAP.Platform.Kernel.Sync.SyncDirection.Down, Id, [nameof(Items.Item.QuantityOnHand)]),
        new("Inventory.ItemCategory", typeof(Items.ItemCategory), ASAP.Platform.Kernel.Sync.SyncDirection.Down, Id),
        new("Inventory.Location", typeof(Locations.Location), ASAP.Platform.Kernel.Sync.SyncDirection.Down, Id),
        new("Inventory.ItemLedgerEntry", typeof(Ledger.ItemLedgerEntry), ASAP.Platform.Kernel.Sync.SyncDirection.Up, Id),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => InventoryMessages.All;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<Locations.LocationBranchLookup>();
        services.AddScoped<StockAvailability>();
        services.AddScoped<Posting.StockPostingService>();
        services.AddScoped<CostSettlementService>();
        services.AddScoped<RevaluationService>();
        services.AddScoped<Items.UnitConversionService>();
        services.AddScoped<Items.UnitSetupService>();
        services.AddScoped<Locations.BinSetupService>();
        services.AddScoped<Seed.InventorySeeder>();
        services.AddScoped<Transfers.TransferService>();
        services.AddScoped<Counting.StockCountService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Item", PermissionAction.Read,
            new LocalizedText("View items", "عرض الأصناف")),

        PermissionDescriptor.Define(
            Id, "Item", PermissionAction.Create,
            new LocalizedText("Add items", "إضافة أصناف"),
            implies: [$"{Id}.Item.Read"]),

        PermissionDescriptor.Define(
            Id, "Item", PermissionAction.Update,
            new LocalizedText("Change items", "تعديل الأصناف"),
            implies: [$"{Id}.Item.Read"]),

        PermissionDescriptor.Define(
            Id, "Unit", PermissionAction.Read,
            new LocalizedText("View units of measure", "عرض وحدات القياس")),

        PermissionDescriptor.Define(
            Id, "Unit", PermissionAction.Update,
            new LocalizedText("Maintain units of measure", "إدارة وحدات القياس"),
            new LocalizedText(
                "Say what the company measures in, and what one item's box holds. A wrong factor "
                + "does not look like an error: it looks like a stock figure.",
                "تحديد ما تقيس به الشركة، وما يحتويه كرتون الصنف. والمعامل الخاطئ لا يبدو خطأً: "
                + "بل يبدو رصيد مخزون."),
            implies: [$"{Id}.Unit.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Location", PermissionAction.Read,
            new LocalizedText("View locations", "عرض المواقع")),

        PermissionDescriptor.Define(
            Id, "Location", PermissionAction.Update,
            new LocalizedText("Maintain locations", "إدارة المواقع"),
            implies: [$"{Id}.Location.Read"]),

        PermissionDescriptor.Define(
            Id, "Bin", PermissionAction.Read,
            new LocalizedText("View bins and what is on them", "عرض الأرفف وما عليها")),

        PermissionDescriptor.Define(
            Id, "Bin", PermissionAction.Update,
            new LocalizedText("Maintain bins", "إدارة الأرفف"),
            new LocalizedText(
                "Lay out the shelves inside a location and say which one goods land in. Nothing "
                + "here changes a valuation: a bin says where stock is, not how much there is.",
                "ترتيب الأرفف داخل الموقع وتحديد أين تصل البضاعة. ولا شيء هنا يغيّر تقييمًا: "
                + "فالرف يحدد مكان المخزون لا كميته."),
            implies: [$"{Id}.Bin.Read"]),

        PermissionDescriptor.Define(
            Id, "Stock", PermissionAction.Read,
            new LocalizedText("View stock levels and movements", "عرض أرصدة وحركات المخزون")),

        PermissionDescriptor.Define(
            Id, "Stock", PermissionAction.Post,
            new LocalizedText("Post stock movements", "ترحيل حركات المخزون"),
            new LocalizedText(
                "Receive, issue and adjust stock. Every movement values itself and posts that "
                + "value to the general ledger.",
                "استلام وصرف وتسوية المخزون. كل حركة تُقيّم نفسها وتُرحّل قيمتها إلى دفتر الأستاذ."),
            implies: [$"{Id}.Stock.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Stock", PermissionAction.Override,
            new LocalizedText("Sell stock that is not there", "بيع مخزون غير متوفر"),
            new LocalizedText(
                "Take stock below zero at a company or item that forbids it, or take it from a "
                + "location whose goods have not been released. Every use is audited, and the cost "
                + "is settled when the goods arrive.",
                "خفض المخزون تحت الصفر في شركة أو صنف يمنع ذلك، أو الصرف من موقع لم يُفرج عن "
                + "بضائعه. يتم تدقيق كل استخدام، وتُسوّى التكلفة عند وصول البضاعة."),
            implies: [$"{Id}.Stock.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Revaluation", PermissionAction.Post,
            new LocalizedText("Write stock up or down", "رفع أو خفض قيمة المخزون"),
            new LocalizedText(
                "Change what stock is worth without changing how much there is. The loss or gain "
                + "reaches the general ledger the moment it is posted.",
                "تغيير قيمة المخزون دون تغيير كميته. وتصل الخسارة أو المكسب إلى دفتر الأستاذ فور "
                + "الترحيل."),
            implies: [$"{Id}.Stock.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Transfer", PermissionAction.Read,
            new LocalizedText("View transfers", "عرض التحويلات")),

        PermissionDescriptor.Define(
            Id, "Transfer", PermissionAction.Post,
            new LocalizedText("Ship and receive transfers", "شحن واستلام التحويلات"),
            implies: [$"{Id}.Transfer.Read", $"{Id}.Stock.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Count", PermissionAction.Read,
            new LocalizedText("See stock counts", "الاطلاع على عمليات الجرد"),
            new LocalizedText(
                "What was counted, when, and what did not match.",
                "ما جُرد ومتى وما لم يطابق.")),

        PermissionDescriptor.Define(
            Id, "Count", PermissionAction.Create,
            new LocalizedText("Start and record a count", "بدء الجرد وتسجيله"),
            implies: [$"{Id}.Count.Read"]),

        PermissionDescriptor.Define(
            Id, "Count", PermissionAction.Post,
            new LocalizedText("Post what a count found", "ترحيل نتيجة الجرد"),
            new LocalizedText(
                "Separate from counting on purpose. A count writes stock off, and the person "
                + "holding the clipboard is not usually the person who should decide that what "
                + "they could not find has gone.",
                "منفصلة عن الجرد عمدًا. فالجرد يشطب مخزونًا، ومن يحمل ورقة الجرد ليس عادةً من "
                + "ينبغي أن يقرر أن ما لم يجده قد فُقد."),
            implies: [$"{Id}.Count.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run inventory reports", "تشغيل تقارير المخزون")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Count.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Stock count numbers", "ترقيم عمليات الجرد"),
            Description = new LocalizedText(
                "The series stock counts are numbered from.",
                "المسلسل الذي تصدر منه أرقام عمليات الجرد."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "COUNT",
            RequiresPermission = $"{Id}.Item.Update",
            HelpTopic = "inventory/stock-count",
        },
        new()
        {
            Key = $"{Id}.Costing.AllowNegativeInventory",
            Module = Id,
            Group = new LocalizedText("Costing", "التكلفة"),
            DisplayName = new LocalizedText("Allow stock to go below zero", "السماح بالمخزون السالب"),
            Description = new LocalizedText(
                "When on, a sale may be made from stock the system does not show as on hand -- "
                + "goods on the shelf whose paperwork has not caught up. The shortfall is valued "
                + "at the item's current cost, marked as an estimate, and settled against the real "
                + "figure when the goods are received. An individual item can override this.",
                "عند التفعيل، يمكن البيع من مخزون لا يُظهره النظام كمتوفر، مثل بضاعة على الرف لم "
                + "تكتمل مستنداتها. يتم تقييم النقص بتكلفة الصنف الحالية ووسمه كتقدير، ثم تسويته "
                + "مقابل التكلفة الفعلية عند الاستلام. ويمكن لكل صنف تجاوز هذا الإعداد."),
            ValueType = SetupValueType.Boolean,
            Scope = SetupScope.Company,
            DefaultValue = "false",
            RequiresPermission = $"{Id}.Stock.Override",
            HelpTopic = "inventory/negative-stock",
        },
        new()
        {
            Key = $"{Id}.Costing.DefaultMethod",
            Module = Id,
            Group = new LocalizedText("Costing", "التكلفة"),
            DisplayName = new LocalizedText("Default costing method", "طريقة التكلفة الافتراضية"),
            Description = new LocalizedText(
                "The method a new item starts with. It can be changed on the item until the first "
                + "entry posts, after which it is fixed.",
                "الطريقة التي يبدأ بها الصنف الجديد. يمكن تغييرها في بطاقة الصنف حتى أول قيد مرحّل، "
                + "وبعدها تصبح ثابتة."),
            ValueType = SetupValueType.Option,
            Scope = SetupScope.Company,
            DefaultValue = "Fifo",
            AllowedValues =
            [
                new SetupOption("Fifo", new LocalizedText("FIFO", "الوارد أولاً صادر أولاً")),
                new SetupOption("Average", new LocalizedText("Weighted average", "المتوسط المرجح")),
                new SetupOption("Standard", new LocalizedText("Standard cost", "التكلفة المعيارية")),
                new SetupOption("Specific", new LocalizedText("Specific", "التكلفة المحددة")),
            ],

            // Locked once anything has posted, because the method decides what every existing
            // value entry meant. Changing it later would not recalculate history.
            IsLockedAfterFirstPosting = true,
            RequiresPermission = $"{Id}.Item.Update",
            HelpTopic = "inventory/costing-methods",
        },
        new()
        {
            Key = $"{Id}.Costing.AutomaticCostAdjustment",
            Module = Id,
            Group = new LocalizedText("Costing", "التكلفة"),
            DisplayName = new LocalizedText(
                "Settle estimated costs automatically",
                "تسوية التكاليف التقديرية تلقائيًا"),
            Description = new LocalizedText(
                "When on, receiving goods immediately settles any sale that ran ahead of them. "
                + "Turn it off only on a very busy installation, where the settlement is better "
                + "run as a scheduled job -- but run it nightly, because until it runs the cost of "
                + "those sales is a guess.",
                "عند التفعيل، يؤدي استلام البضاعة فورًا إلى تسوية أي بيع سبقها. لا تعطّله إلا في "
                + "التركيبات المزدحمة جدًا حيث يُفضّل تشغيل التسوية كمهمة مجدولة، مع تشغيلها ليليًا، "
                + "لأن تكلفة تلك المبيعات تبقى تقديرًا حتى تُشغّل."),
            ValueType = SetupValueType.Boolean,
            Scope = SetupScope.Company,
            DefaultValue = "true",
            RequiresPermission = $"{Id}.Stock.Post",
            HelpTopic = "inventory/cost-adjustment",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Inventory.Root",
            Module = Id,
            DisplayName = new LocalizedText("Inventory", "المخزون"),
            Kind = NavigationKind.Group,
            Icon = "inventory",
            Order = 200,
        },
        Page("Items", new LocalizedText("Items", "الأصناف"), "/inventory/items", $"{Id}.Item.Read", 10),
        Page(
            "Bins",
            new LocalizedText("Bins", "الأرفف"),
            "/inventory/bins",
            $"{Id}.Bin.Read",
            22),
        Page(
            "Units",
            new LocalizedText("Units of measure", "وحدات القياس"),
            "/inventory/units",
            $"{Id}.Unit.Read",
            25),
        Page(
            "Locations",
            new LocalizedText("Locations", "المواقع"),
            "/inventory/locations",
            $"{Id}.Location.Read",
            20),
        Page(
            "Movements",
            new LocalizedText("Stock movements", "حركات المخزون"),
            "/inventory/movements",
            $"{Id}.Stock.Read",
            30),
        Page(
            "Transfers",
            new LocalizedText("Transfers", "التحويلات"),
            "/inventory/transfers",
            $"{Id}.Transfer.Read",
            35),
        new()
        {
            Id = "Inventory.StockCounts",
            Module = Id,
            ParentId = "Inventory.Root",
            DisplayName = new LocalizedText("Stock counts", "الجرد"),
            Kind = NavigationKind.Page,
            Route = "/inventory/counts",
            RequiresPermission = $"{Id}.Count.Read",
            Order = 35,
        },
        new()
        {
            Id = "Inventory.StockOnHand",
            Module = Id,
            ParentId = "Inventory.Root",
            DisplayName = new LocalizedText("Stock on hand", "الأرصدة المتوفرة"),
            Kind = NavigationKind.Report,
            Route = "/inventory/reports/stock-on-hand",
            RequiresPermission = $"{Id}.Report.Read",
            Order = 40,
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
            ParentId = "Inventory.Root",
            DisplayName = displayName,
            Route = route,
            RequiresPermission = permission,
            Order = order,
        };
}
