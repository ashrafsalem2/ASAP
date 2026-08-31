using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Promotions;

/// <summary>
/// The Promotions module: offers, the rules that pick between them, and the floor they cannot go
/// below.
/// </summary>
public sealed class PromotionsModule : IAsapModule, ISyncContributor
{
    /// <summary>The module identifier used in every Promotions permission and setting key.</summary>
    public const string Id = "Promotions";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Promotions", "العروض الترويجية");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Offers priced against live cost, so a campaign written last month against last month's "
        + "costs cannot quietly run at a loss.",
        "عروض تُسعَّر مقابل التكلفة الفعلية، فلا تتحول حملة أُعدت الشهر الماضي بتكاليف ذلك الوقت "
        + "إلى خسارة صامتة.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// Promotions sits above Inventory and Finance, and below anything that sells.
    /// </summary>
    /// <remarks>
    /// It needs what things cost, which Inventory owns, and somewhere to post what it gives away,
    /// which Finance owns. Sales and point of sale call it rather than the other way round: an
    /// offer is a rule about pricing, and pricing happens before either of them posts anything.
    /// See docs/architecture/module-dependencies.md.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn =>
    [
        Platform.Core.Modules.PlatformModule.Id,
        Modules.Inventory.InventoryModule.Id,
        Modules.Finance.FinanceModule.Id,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => PromotionsMessages.All;

    /// <summary>
    /// Offers travel down to branches; what they gave away travels up inside the receipts.
    /// </summary>
    /// <remarks>
    /// A shop must not invent its own offers. The whole value of a promotion is that head office
    /// knows what it cost, and a branch that could write one would be a branch whose margin nobody
    /// can explain.
    /// </remarks>
    public IReadOnlyCollection<SyncEntityDescriptor> SyncEntities =>
    [
        new("Promotions.Offer", typeof(Offers.Offer), SyncDirection.Down, Id),
        new("Promotions.OfferTarget", typeof(Offers.OfferTarget), SyncDirection.Down, Id),
    ];

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<Pricing.PromotionEngine>();
        services.AddScoped<Offers.OfferService>();
        services.AddScoped<Reporting.PromotionReportService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Offer", PermissionAction.Read,
            new LocalizedText("View offers", "عرض العروض الترويجية")),

        PermissionDescriptor.Define(
            Id, "Offer", PermissionAction.Create,
            new LocalizedText("Create offers", "إنشاء عروض"),
            implies: [$"{Id}.Offer.Read"]),

        PermissionDescriptor.Define(
            Id, "Offer", PermissionAction.Update,
            new LocalizedText("Change offers", "تعديل العروض"),
            implies: [$"{Id}.Offer.Read"]),

        PermissionDescriptor.Define(
            Id, "Offer", PermissionAction.Override,
            new LocalizedText("Approve an offer that sells below the floor", "اعتماد عرض يقل عن الحد الأدنى للهامش"),
            new LocalizedText(
                "Run an offer that leaves less margin than the company accepts, or none at all. "
                + "Clearing old stock is a real reason to do it; every use is audited.",
                "تشغيل عرض يترك هامشًا أقل مما تقبله الشركة أو لا يترك هامشًا. وتصريف المخزون "
                + "القديم سبب وجيه لذلك، ويتم تدقيق كل استخدام."),
            implies: [$"{Id}.Offer.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run promotion reports", "تشغيل تقارير العروض")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Margin.FloorPercent",
            Module = Id,
            Group = new LocalizedText("Margin", "الهامش"),
            DisplayName = new LocalizedText("Least margin an offer may leave", "أقل هامش يتركه العرض"),
            Description = new LocalizedText(
                "As a percentage of the selling price, which is what a gross margin is and what "
                + "every report the company already runs will compare it to. Zero means never "
                + "below cost, which is what most shops want. Checked against live cost every "
                + "time an offer is applied, not once when it was written: suppliers put prices "
                + "up, and a campaign priced against last quarter's costs is exactly how a shop "
                + "runs a loss-making fortnight without noticing.",
                "كنسبة من سعر البيع، وهو تعريف الهامش الإجمالي وما ستقارنه به تقارير الشركة "
                + "القائمة. والقيمة صفر تعني عدم البيع بأقل من التكلفة، وهو ما تريده أغلب المتاجر. "
                + "ويُفحص مقابل التكلفة الفعلية عند كل تطبيق للعرض لا مرة واحدة عند إعداده، فالموردون "
                + "يرفعون الأسعار، والحملة المسعّرة بتكاليف الربع الماضي هي بالضبط كيف يمضي متجر "
                + "أسبوعين بخسارة دون أن ينتبه."),
            ValueType = SetupValueType.Decimal,
            Scope = SetupScope.Company,
            DefaultValue = "0",
            RequiresPermission = $"{Id}.Offer.Update",
            HelpTopic = "promotions/margin",
        },
        new()
        {
            Key = $"{Id}.Posting.DiscountAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Promotions given away", "قيمة العروض الممنوحة"),
            Description = new LocalizedText(
                "Where what an offer takes off is posted. Deliberately separate from the ordinary "
                + "sales discount: both are money given away, and only one of them is a campaign "
                + "somebody chose to run and should be able to total the cost of.",
                "الحساب الذي تُرحّل إليه قيمة ما تخصمه العروض. وهو منفصل عمدًا عن خصم المبيعات "
                + "العادي، فكلاهما مبالغ ممنوحة، لكن أحدهما حملة اختيرت عمدًا ويجب أن يمكن حصر "
                + "تكلفتها."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "4350",
            RequiresPermission = $"{Id}.Offer.Update",
            HelpTopic = "promotions/setup",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Promotions.Root",
            Module = Id,
            DisplayName = new LocalizedText("Promotions", "العروض الترويجية"),
            Kind = NavigationKind.Group,
            Icon = "promotions",
            Order = 450,
        },
        new()
        {
            Id = "Promotions.Reports",
            Module = Id,
            ParentId = "Promotions.Root",
            DisplayName = new LocalizedText("Offer reports", "تقارير العروض"),
            Kind = NavigationKind.Report,
            Route = "/promotions/reports",
            RequiresPermission = $"{Id}.Offer.Read",
            Order = 20,
            HelpTopic = "promotions/reports",
        },
        new()
        {
            Id = "Promotions.Offers",
            Module = Id,
            ParentId = "Promotions.Root",
            DisplayName = new LocalizedText("Offers", "العروض"),
            Kind = NavigationKind.Page,
            Route = "/promotions/offers",
            RequiresPermission = $"{Id}.Offer.Read",
            Order = 10,
        },
    ];
}
