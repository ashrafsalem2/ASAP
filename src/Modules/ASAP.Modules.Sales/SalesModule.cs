using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Sales;

/// <summary>
/// The Sales module: orders, shipments, invoices, and the checks that guard them.
/// </summary>
public sealed class SalesModule : IAsapModule
{
    /// <summary>The module identifier used in every Sales permission and setting key.</summary>
    public const string Id = "Sales";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Sales", "المبيعات");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Sales orders, shipments and invoices, with availability checked against stock and credit "
        + "checked against the customer.",
        "أوامر البيع والشحنات والفواتير، مع التحقق من توفر المخزون وحد ائتمان العميل.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// Sales sits above Inventory and Finance, exactly as Purchasing does.
    /// </summary>
    /// <remarks>
    /// A shipment is a stock movement and an invoice is a customer ledger entry. See
    /// docs/architecture/module-dependencies.md.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn =>
    [
        Platform.Core.Modules.PlatformModule.Id,
        Modules.Inventory.InventoryModule.Id,
        Modules.Finance.FinanceModule.Id,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => SalesMessages.All;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<Orders.SalesOrderService>();


        services.AddScoped<Pricing.PricingService>();
        services.AddScoped<Quotes.SalesQuoteService>();
        services.AddScoped<Orders.SalesReturnService>();
        services.AddScoped<Reporting.SalesReportService>();
        services.AddScoped<ASAP.Platform.Kernel.Documents.IDocumentParties, Reporting.SalesDocumentParties>();
        services.AddScoped<Orders.SalesShipmentService>();
        services.AddScoped<Orders.SalesInvoiceService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Quote", PermissionAction.Read,
            new LocalizedText("View quotes", "عرض عروض الأسعار")),

        PermissionDescriptor.Define(
            Id, "Quote", PermissionAction.Create,
            new LocalizedText("Offer a customer a price", "تقديم سعر للعميل"),
            new LocalizedText(
                "A quote commits nothing and moves nothing, so it sits with whoever talks to "
                + "customers rather than with whoever posts documents.",
                "عرض السعر لا يلزم بشيء ولا يحرك شيئًا، فمكانه مع من يتحدث إلى العملاء لا مع "
                + "من يرحّل المستندات."),
            implies: [$"{Id}.Quote.Read"]),


        PermissionDescriptor.Define(
            Id, "Return", PermissionAction.Post,
            new LocalizedText("Take goods back and credit the customer", "استعادة البضاعة وتقييد العميل دائنًا"),
            new LocalizedText(
                "A credit memo takes money off what a customer owes and puts stock back on the "
                + "shelf. Both are worth separating from whoever raised the invoice.",
                "إشعار الدائن يخصم من مديونية العميل ويعيد المخزون إلى الرف. وكلاهما جدير "
                + "بالفصل عمن أصدر الفاتورة."),
            isSensitive: true),


        PermissionDescriptor.Define(
            Id, "PriceList", PermissionAction.Read,
            new LocalizedText("View price lists", "عرض قوائم الأسعار")),

        PermissionDescriptor.Define(
            Id, "PriceList", PermissionAction.Update,
            new LocalizedText("Maintain price lists", "إدارة قوائم الأسعار"),
            new LocalizedText(
                "Say what each customer pays. This is the commercial arrangement itself, so it "
                + "belongs with whoever agrees prices rather than with whoever types orders.",
                "تحديد ما يدفعه كل عميل. وهذا هو الاتفاق التجاري نفسه، فمكانه مع من يتفق على "
                + "الأسعار لا مع من يُدخل الأوامر."),
            implies: [$"{Id}.PriceList.Read"],
            isSensitive: true),


        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Read,
            new LocalizedText("View sales orders", "عرض أوامر البيع")),

        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Create,
            new LocalizedText("Take sales orders", "تسجيل أوامر البيع"),
            implies: [$"{Id}.Order.Read"]),

        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Update,
            new LocalizedText("Change sales orders", "تعديل أوامر البيع"),
            implies: [$"{Id}.Order.Read"]),

        PermissionDescriptor.Define(
            Id, "Shipment", PermissionAction.Post,
            new LocalizedText("Ship goods", "شحن البضائع"),
            new LocalizedText(
                "Record that goods have left. This takes stock out and charges what it cost to "
                + "cost of sales.",
                "تسجيل خروج البضاعة. يخصم هذا من المخزون ويُحمّل تكلفته على تكلفة المبيعات."),
            implies: [$"{Id}.Order.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Invoice", PermissionAction.Post,
            new LocalizedText("Post sales invoices", "ترحيل فواتير المبيعات"),
            new LocalizedText(
                "Turn what has shipped into a debt the customer owes. Separate from shipping, "
                + "because deciding what to charge is not the same job as packing a box.",
                "تحويل ما شُحن إلى دين على العميل. وهي منفصلة عن الشحن، لأن تحديد ما يُحاسَب عليه "
                + "ليس نفس عمل تجهيز الشحنة."),
            implies: [$"{Id}.Order.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Override,
            new LocalizedText(
                "Sell to a blocked customer, or past what the order allows",
                "البيع لعميل محظور أو بما يتجاوز أمر البيع"),
            new LocalizedText(
                "Take an order from a customer whose account is stopped, ship more than was "
                + "ordered, or invoice more than shipped. Every use is audited.",
                "تسجيل أمر لعميل موقوف الحساب، أو شحن أكثر من المطلوب، أو فوترة أكثر مما شُحن. "
                + "ويتم تدقيق كل استخدام."),
            implies: [$"{Id}.Order.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run sales reports", "تشغيل تقارير المبيعات")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Posting.RevenueAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Sales revenue", "إيرادات المبيعات"),
            Description = new LocalizedText(
                "Where the value of what is sold is credited. Item lines can be routed to their "
                + "own accounts through the item category later; this is what everything else "
                + "posts to.",
                "الحساب الذي تُرحّل إليه قيمة المبيعات. ويمكن لاحقًا توجيه سطور الأصناف إلى "
                + "حسابات خاصة عبر تصنيف الصنف، وهذا هو الحساب الافتراضي لما عداها."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "4100",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.DiscountAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Discounts given", "الخصومات الممنوحة"),
            Description = new LocalizedText(
                "Where discounts are posted, so what the company gives away is visible in its own "
                + "right. Netting a discount off revenue hides it: the profit is the same and "
                + "nobody can answer how much was discounted last quarter.",
                "الحساب الذي تُرحّل إليه الخصومات، ليظهر ما تتنازل عنه الشركة بشكل مستقل. فخصمها "
                + "من الإيراد مباشرة يُخفيها: الربح واحد، لكن لا أحد يستطيع الإجابة عن حجم "
                + "الخصومات في الربع الماضي."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "4300",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/setup",
        },
        new()
        {
            Key = $"{Id}.Orders.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Sales order numbers", "ترقيم أوامر البيع"),
            Description = new LocalizedText(
                "The series sales order numbers are issued from.",
                "المسلسل الذي تصدر منه أرقام أوامر البيع."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "SALES-ORD",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/setup",
        },
        new()
        {
            Key = $"{Id}.Invoices.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Sales invoice numbers", "ترقيم فواتير المبيعات"),
            Description = new LocalizedText(
                "The series invoice numbers are issued from. Use a gapless series: a tax invoice "
                + "sequence with holes in it is a question from the authority.",
                "المسلسل الذي تصدر منه أرقام الفواتير. استخدم مسلسلاً بلا فجوات، فتسلسل الفواتير "
                + "الضريبية المتقطع يستدعي استفسارًا من الهيئة."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "SALES-INV",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/setup",
        },
        new()
        {
            Key = $"{Id}.CreditMemos.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Credit memo numbers", "ترقيم إشعارات الدائن"),
            Description = new LocalizedText(
                "The series credit memo numbers are issued from. Keep it separate from the invoice "
                + "series and keep it gapless for the same reason: a credit memo is a tax document "
                + "too, and a sequence with holes in it is a question from the authority.",
                "المسلسل الذي تصدر منه أرقام إشعارات الدائن. اجعله منفصلًا عن مسلسل الفواتير "
                + "وبلا فجوات للسبب نفسه: فإشعار الدائن مستند ضريبي أيضًا، والتسلسل المتقطع "
                + "يستدعي استفسارًا من الهيئة."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "SALES-CM",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/setup",
        },
        new()
        {
            Key = $"{Id}.Quotes.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Quote numbers", "ترقيم عروض الأسعار"),
            Description = new LocalizedText(
                "The series quote numbers are issued from. A quote is not a tax document, so gaps "
                + "in it cost nothing and the series can be gap-tolerant.",
                "المسلسل الذي تصدر منه أرقام عروض الأسعار. وعرض السعر ليس مستندًا ضريبيًا، فلا "
                + "ضير في فجواته ويجوز أن يكون المسلسل متسامحًا معها."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "SALES-QTE",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/setup",
        },
        new()
        {
            Key = $"{Id}.Quotes.ValidForDays",
            Module = Id,
            Group = new LocalizedText("Quotes", "عروض الأسعار"),
            DisplayName = new LocalizedText("How long a quote stands", "مدة سريان عرض السعر"),
            Description = new LocalizedText(
                "How many days a quote holds its prices for, unless somebody says otherwise on "
                + "the quote itself. Costs move and suppliers put prices up, so a quote that "
                + "never ran out would be a price the company could never withdraw.",
                "كم يومًا يحتفظ عرض السعر بأسعاره، ما لم يُحدَّد غير ذلك على العرض نفسه. "
                + "فالتكاليف تتحرك والموردون يرفعون أسعارهم، والعرض الذي لا ينتهي سعرٌ لا "
                + "تستطيع الشركة سحبه أبدًا."),
            ValueType = SetupValueType.Integer,
            Scope = SetupScope.Company,
            DefaultValue = "30",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "sales/quotes",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Sales.Root",
            Module = Id,
            DisplayName = new LocalizedText("Sales", "المبيعات"),
            Kind = NavigationKind.Group,
            Icon = "sales",
            Order = 400,
        },
        new()
        {
            Id = "Sales.Quotes",
            Module = Id,
            ParentId = "Sales.Root",
            DisplayName = new LocalizedText("Quotes", "عروض الأسعار"),
            Kind = NavigationKind.Page,
            Route = "/sales/quotes",
            RequiresPermission = $"{Id}.Quote.Read",
            Order = 5,
            HelpTopic = "sales/quotes",
        },
        new()
        {
            Id = "Sales.PriceLists",
            Module = Id,
            ParentId = "Sales.Root",
            DisplayName = new LocalizedText("Price lists", "قوائم الأسعار"),
            Kind = NavigationKind.Page,
            Route = "/sales/price-lists",
            RequiresPermission = $"{Id}.PriceList.Read",
            Order = 60,
            HelpTopic = "sales/price-lists",
        },
        new()
        {
            Id = "Sales.Reports",
            Module = Id,
            ParentId = "Sales.Root",
            DisplayName = new LocalizedText("Sales reports", "تقارير المبيعات"),
            Kind = NavigationKind.Page,
            Route = "/sales/reports",
            RequiresPermission = $"{Id}.Order.Read",
            Order = 70,

            // A report refuses nothing, so it has no message to hang its documentation off.
            HelpTopic = "sales/reports",
        },
        new()
        {
            Id = "Sales.Orders",
            Module = Id,
            ParentId = "Sales.Root",
            DisplayName = new LocalizedText("Sales orders", "أوامر البيع"),
            Kind = NavigationKind.Page,
            Route = "/sales/orders",
            RequiresPermission = $"{Id}.Order.Read",
            Order = 10,
        },
    ];
}
