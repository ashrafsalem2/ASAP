using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Purchasing;

/// <summary>
/// The Purchasing module: purchase orders, receipts, invoices and the match between them.
/// </summary>
public sealed class PurchasingModule : IAsapModule
{
    /// <summary>The module identifier used in every Purchasing permission and setting key.</summary>
    public const string Id = "Purchasing";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Purchasing", "المشتريات");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Purchase orders, goods receipts, vendor invoices, and the three-way match that checks "
        + "them against each other.",
        "أوامر الشراء واستلام البضائع وفواتير المورّدين، والمطابقة الثلاثية بينها.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// Purchasing sits above Inventory and Finance rather than beside them.
    /// </summary>
    /// <remarks>
    /// A receipt is a stock movement and an invoice is a vendor ledger entry; there is no company
    /// that owns Purchasing but not the things it posts into. Inventory and Finance remain
    /// siblings and still trade through the kernel, because either of those can be owned without
    /// the other. See docs/architecture/module-dependencies.md.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn =>
    [
        Platform.Core.Modules.PlatformModule.Id,
        Modules.Inventory.InventoryModule.Id,
        Modules.Finance.FinanceModule.Id,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => PurchasingMessages.All;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<Orders.PurchaseOrderService>();


        services.AddScoped<Orders.PurchaseReturnService>();
        services.AddScoped<Requisitions.PurchaseRequisitionService>();


        services.AddScoped<Approvals.PurchaseApprovalService>();
        services.AddScoped<Costing.LandedCostService>();
        services.AddScoped<Reporting.PurchaseReportService>();
        services.AddScoped<Orders.PurchaseReceiptService>();
        services.AddScoped<Orders.PurchaseInvoiceService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Requisition", PermissionAction.Read,
            new LocalizedText("View requisitions", "عرض طلبات الشراء")),

        PermissionDescriptor.Define(
            Id, "Requisition", PermissionAction.Create,
            new LocalizedText("Ask for something to be bought", "طلب شراء شيء"),
            new LocalizedText(
                "A requisition commits nothing and posts nothing, so it belongs with whoever "
                + "runs out of things rather than with whoever places orders.",
                "طلب الشراء لا يلزم بشيء ولا يرحّل شيئًا، فمكانه مع من ينفد ما عنده لا مع من "
                + "يضع الأوامر."),
            implies: [$"{Id}.Requisition.Read"]),

        PermissionDescriptor.Define(
            Id, "Requisition", PermissionAction.Approve,
            new LocalizedText("Sign for a requisition", "التوقيع على طلب شراء"),
            new LocalizedText(
                "Nobody signs for their own request, whatever this permission says. An approval "
                + "you can give yourself is a checkbox rather than a control.",
                "لا يوقّع أحد على طلبه هو، مهما قالت هذه الصلاحية. فالموافقة التي تمنحها لنفسك "
                + "خانة تأشير لا ضابط."),
            isSensitive: true),


        PermissionDescriptor.Define(
            Id, "Return", PermissionAction.Post,
            new LocalizedText("Send goods back to a vendor", "إعادة البضاعة إلى المورد"),
            new LocalizedText(
                "Sending goods back takes stock off the shelf and money off what the company "
                + "owes. Both are worth separating from whoever received the delivery.",
                "إعادة البضاعة تخصم مخزونًا من الرف ومالًا مما تدين به الشركة. وكلاهما جدير "
                + "بالفصل عمن استلم التوريد."),
            isSensitive: true),


        PermissionDescriptor.Define(
            Id, "LandedCost", PermissionAction.Post,
            new LocalizedText("Apply landed cost", "تحميل تكلفة التوريد"),
            new LocalizedText(
                "Add freight, duty and clearance to the cost of the goods they were spent on. "
                + "Where some of those goods have already been sold, this corrects what that sale "
                + "cost as well as what the stock is worth.",
                "إضافة الشحن والرسوم والتخليص إلى تكلفة البضاعة التي أُنفقت عليها. وحيث بيع شيء "
                + "منها، يصحّح هذا تكلفة تلك البيعة إلى جانب قيمة المخزون."),
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Approval", PermissionAction.Read,
            new LocalizedText("View approval limits", "عرض حدود الاعتماد")),

        PermissionDescriptor.Define(
            Id, "Approval", PermissionAction.Update,
            new LocalizedText("Set approval limits", "تحديد حدود الاعتماد"),
            new LocalizedText(
                "Say how much each person may sign a purchase order for. This is the authority "
                + "itself, so whoever holds it can grant themselves any amount -- keep it with "
                + "whoever would sign for the limits on paper.",
                "تحديد المبلغ الذي يجوز لكل شخص التوقيع به على أمر شراء. وهذه هي الصلاحية نفسها، "
                + "فمن يملكها يستطيع منح نفسه أي مبلغ — فلتكن مع من يوقّع على الحدود ورقًا."),
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Approval", PermissionAction.Post,
            new LocalizedText("Approve purchase orders", "اعتماد أوامر الشراء"),
            new LocalizedText(
                "Sign for an order, up to your own limit and never one you raised yourself.",
                "التوقيع على أمر، في حدود صلاحيتك، ولا يكون أمرًا أصدرته بنفسك."),
            isSensitive: true),


        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Read,
            new LocalizedText("View purchase orders", "عرض أوامر الشراء")),

        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Create,
            new LocalizedText("Raise purchase orders", "إنشاء أوامر الشراء"),
            implies: [$"{Id}.Order.Read"]),

        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Update,
            new LocalizedText("Change purchase orders", "تعديل أوامر الشراء"),
            implies: [$"{Id}.Order.Read"]),

        PermissionDescriptor.Define(
            Id, "Receipt", PermissionAction.Post,
            new LocalizedText("Receive goods", "استلام البضائع"),
            new LocalizedText(
                "Record that goods have arrived. This moves stock and records what the company "
                + "owes for it, before any invoice exists.",
                "تسجيل وصول البضاعة. يحرّك هذا المخزون ويسجّل ما يستحق على الشركة قبل وجود أي فاتورة."),
            implies: [$"{Id}.Order.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Invoice", PermissionAction.Post,
            new LocalizedText("Post vendor invoices", "ترحيل فواتير المورّدين"),
            new LocalizedText(
                "Turn what has arrived into a debt owed to the vendor. Deliberately separate from "
                + "receiving: the storeman who signs for a delivery is not the person who agrees "
                + "the company should pay for it.",
                "تحويل ما وصل إلى دين مستحق للمورّد. وهي منفصلة عمدًا عن الاستلام: فأمين المستودع "
                + "الذي يوقّع على التسليم ليس من يقرّ بأن على الشركة السداد."),
            implies: [$"{Id}.Order.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Order", PermissionAction.Override,
            new LocalizedText(
                "Receive or invoice past what the order allows",
                "الاستلام أو الفوترة بما يتجاوز أمر الشراء"),
            new LocalizedText(
                "Accept more goods than were ordered, invoice more than arrived, or order from a "
                + "blocked vendor. Every use is audited.",
                "قبول بضاعة أكثر من المطلوب، أو فوترة أكثر مما وصل، أو الشراء من مورّد محظور. "
                + "ويتم تدقيق كل استخدام."),
            implies: [$"{Id}.Order.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run purchasing reports", "تشغيل تقارير المشتريات")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Requisitions.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Requisition numbers", "ترقيم طلبات الشراء"),
            Description = new LocalizedText(
                "The series requisition numbers are issued from. A requisition commits nothing, "
                + "so gaps in it cost nothing.",
                "المسلسل الذي تصدر منه أرقام طلبات الشراء. وطلب الشراء لا يُلزم بشيء، فلا ضير "
                + "في فجواته."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "PURCH-REQ",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "purchasing/setup",
        },
        new()
        {
            Key = $"{Id}.CreditMemos.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Credit memo numbers", "ترقيم إشعارات الدائن"),
            Description = new LocalizedText(
                "The series numbers are issued from when goods go back to a vendor and the debt "
                + "is reduced.",
                "المسلسل الذي تصدر منه الأرقام حين تعود البضاعة إلى المورد ويُخفَّض الدين."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "PURCH-CM",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "purchasing/setup",
        },
        new()
        {
            Key = $"{Id}.Approval.Threshold",
            Module = Id,
            Group = new LocalizedText("Approvals", "الاعتمادات"),
            DisplayName = new LocalizedText(
                "Goes to the vendor unsigned up to",
                "يُرسل إلى المورّد بلا توقيع حتى"),
            Description = new LocalizedText(
                "An order worth more than this waits for somebody whose approval limit covers it, "
                + "and never for the person who raised it. Nought means everything is signed for. "
                + "There is no way to switch approvals off other than setting this high, which is "
                + "deliberate: it leaves the decision visible as a number somebody chose rather "
                + "than as a feature nobody turned on.",
                "الأمر الذي تزيد قيمته على هذا ينتظر من يغطي حد اعتماده المبلغ، ولا ينتظر أبدًا "
                + "من أصدره. والصفر يعني التوقيع على كل شيء. ولا سبيل لإيقاف الاعتمادات إلا برفع "
                + "هذا الرقم، وهذا مقصود: فهو يُبقي القرار ظاهرًا رقمًا اختاره أحدهم لا خاصية لم "
                + "يشغّلها أحد."),
            ValueType = SetupValueType.Decimal,
            Scope = SetupScope.Company,
            DefaultValue = "10000",
            RequiresPermission = $"{Id}.Approval.Update",
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Key = $"{Id}.Posting.AccrualAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText(
                "Goods received not invoiced",
                "بضاعة مستلمة غير مفوترة"),
            Description = new LocalizedText(
                "Where the value of received goods waits until the invoice arrives. The company "
                + "owes for goods from the moment they land, and holding that in its own account "
                + "is what keeps the balance sheet right at month end rather than a fortnight "
                + "behind the post.",
                "الحساب الذي تنتظر فيه قيمة البضاعة المستلمة حتى تصل الفاتورة. فالشركة مدينة "
                + "بقيمة البضاعة من لحظة وصولها، وحفظ ذلك في حساب مستقل هو ما يجعل الميزانية "
                + "صحيحة في نهاية الشهر بدلاً من أن تتأخر بانتظار البريد."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "2300",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "purchasing/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.VarianceAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Purchase price variance", "فروق أسعار المشتريات"),
            Description = new LocalizedText(
                "Where the difference goes when a vendor invoices at a price other than the one "
                + "ordered. Kept visible rather than absorbed into stock, so somebody can see how "
                + "often it happens and with whom.",
                "الحساب الذي يُرحّل إليه الفارق عندما يفوتر المورّد بسعر يخالف السعر المطلوب. "
                + "ويبقى ظاهرًا بدلاً من دمجه في تكلفة المخزون، ليتضح تكرار ذلك ومع أي مورّد."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "5300",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "purchasing/setup",
        },
        new()
        {
            Key = $"{Id}.Orders.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Purchase order numbers", "ترقيم أوامر الشراء"),
            Description = new LocalizedText(
                "The series purchase order numbers are issued from.",
                "المسلسل الذي تصدر منه أرقام أوامر الشراء."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "PURCH-ORD",
            RequiresPermission = $"{Id}.Order.Update",
            HelpTopic = "purchasing/setup",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Purchasing.Root",
            Module = Id,
            DisplayName = new LocalizedText("Purchasing", "المشتريات"),
            Kind = NavigationKind.Group,
            Icon = "purchasing",
            Order = 300,
        },
        new()
        {
            Id = "Purchasing.Requisitions",
            Module = Id,
            ParentId = "Purchasing.Root",
            DisplayName = new LocalizedText("Requisitions", "طلبات الشراء"),
            Kind = NavigationKind.Page,
            Route = "/purchasing/requisitions",
            RequiresPermission = $"{Id}.Requisition.Read",
            Order = 5,
            HelpTopic = "purchasing/requisitions",
        },
        new()
        {
            Id = "Purchasing.Orders",
            Module = Id,
            ParentId = "Purchasing.Root",
            DisplayName = new LocalizedText("Purchase orders", "أوامر الشراء"),
            Kind = NavigationKind.Page,
            Route = "/purchasing/orders",
            RequiresPermission = $"{Id}.Order.Read",
            Order = 10,
        },
        new()
        {
            Id = "Purchasing.Reports",
            Module = Id,
            ParentId = "Purchasing.Root",
            DisplayName = new LocalizedText("Purchase reports", "تقارير المشتريات"),
            Kind = NavigationKind.Page,
            Route = "/purchasing/reports",
            RequiresPermission = $"{Id}.Order.Read",
            Order = 70,

            // A report refuses nothing, so it has no message to hang its documentation off.
            HelpTopic = "purchasing/reports",
        },
        new()
        {
            Id = "Purchasing.ApprovalLimits",
            Module = Id,
            ParentId = "Purchasing.Root",
            DisplayName = new LocalizedText("Approval limits", "حدود الاعتماد"),
            Kind = NavigationKind.Page,
            Route = "/purchasing/approval-limits",
            RequiresPermission = $"{Id}.Approval.Read",
            Order = 80,
        },
    ];
}
