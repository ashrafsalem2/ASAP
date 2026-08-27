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
        services.AddScoped<Orders.PurchaseReceiptService>();
        services.AddScoped<Orders.PurchaseInvoiceService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
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
            Id = "Purchasing.Orders",
            Module = Id,
            ParentId = "Purchasing.Root",
            DisplayName = new LocalizedText("Purchase orders", "أوامر الشراء"),
            Kind = NavigationKind.Page,
            Route = "/purchasing/orders",
            RequiresPermission = $"{Id}.Order.Read",
            Order = 10,
        },
    ];
}
