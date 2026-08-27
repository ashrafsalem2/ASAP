using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Sales;

/// <summary>Everything the Sales module can tell the user.</summary>
public static class SalesMessages
{
    /// <summary>The order named does not exist.</summary>
    public static readonly MessageCode OrderNotFound = new("SAL.ORDER.NOT_FOUND");

    /// <summary>An order was submitted with nothing on it.</summary>
    public static readonly MessageCode OrderHasNoLines = new("SAL.ORDER.NO_LINES");

    /// <summary>The customer named does not exist.</summary>
    public static readonly MessageCode CustomerNotFound = new("SAL.ORDER.CUSTOMER_NOT_FOUND");

    /// <summary>The customer has been withdrawn from use.</summary>
    public static readonly MessageCode CustomerBlocked = new("SAL.ORDER.CUSTOMER_BLOCKED");

    /// <summary>A line names an item that does not exist.</summary>
    public static readonly MessageCode ItemNotFound = new("SAL.ORDER.ITEM_NOT_FOUND");

    /// <summary>A line carries no quantity.</summary>
    public static readonly MessageCode QuantityZero = new("SAL.ORDER.QUANTITY_ZERO");

    /// <summary>A line sells stock and names nowhere to ship it from.</summary>
    public static readonly MessageCode NoLocation = new("SAL.ORDER.NO_LOCATION");

    /// <summary>There is nothing left on the order to ship.</summary>
    public static readonly MessageCode NothingToShip = new("SAL.SHIPMENT.NOTHING_OUTSTANDING");

    /// <summary>A shipment names more than was ordered.</summary>
    public static readonly MessageCode OverShipment = new("SAL.SHIPMENT.MORE_THAN_ORDERED");

    /// <summary>There is nothing shipped that still needs invoicing.</summary>
    public static readonly MessageCode NothingToInvoice = new("SAL.INVOICE.NOTHING_OUTSTANDING");

    /// <summary>An invoice names more than has been shipped.</summary>
    public static readonly MessageCode InvoiceExceedsShipment = new("SAL.INVOICE.MORE_THAN_SHIPPED");

    /// <summary>A line would be sold below what the goods cost.</summary>
    public static readonly MessageCode BelowCost = new("SAL.ORDER.BELOW_COST");

    /// <summary>The revenue account has not been set up.</summary>
    public static readonly MessageCode NoRevenueAccount = new("SAL.SETUP.NO_REVENUE_ACCOUNT");

    /// <summary>Every message the module declares.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = OrderNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such sales order", "لا يوجد أمر بيع بهذا الرقم"),
            Detail = new LocalizedText(
                "Nothing in this company is numbered {OrderNo}.",
                "لا يوجد في هذه الشركة أمر بيع يحمل الرقم {OrderNo}."),
            Resolution = new LocalizedText(
                "Choose the order from the list rather than typing its number.",
                "اختر الأمر من القائمة بدلاً من كتابة رقمه."),
        },
        new()
        {
            Code = OrderHasNoLines,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The order has no lines", "أمر البيع بدون سطور"),
            Detail = new LocalizedText(
                "An order for {CustomerNo} was submitted with nothing on it.",
                "تم إرسال أمر بيع للعميل {CustomerNo} بدون أي أصناف."),
            Resolution = new LocalizedText(
                "Add at least one item or charge, with a quantity and a price.",
                "أضف صنفًا أو رسمًا واحدًا على الأقل، مع الكمية والسعر."),
        },
        new()
        {
            Code = CustomerNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such customer", "لا يوجد عميل بهذا الرقم"),
            Detail = new LocalizedText(
                "No customer in this company is numbered {CustomerNo}.",
                "لا يوجد عميل في هذه الشركة يحمل الرقم {CustomerNo}."),
            Resolution = new LocalizedText(
                "Check the number against the customer list, or create {CustomerNo} first.",
                "تحقق من الرقم في قائمة العملاء، أو أنشئ {CustomerNo} أولاً."),
        },
        new()
        {
            Code = CustomerBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That customer is blocked", "هذا العميل محظور"),
            Detail = new LocalizedText(
                "{CustomerNo} {CustomerName} has been withdrawn from use, which usually means "
                + "somebody stopped their account.",
                "تم سحب {CustomerNo} {CustomerName} من الاستخدام، وعادةً ما يعني ذلك أن حسابه أُوقف."),
            Resolution = new LocalizedText(
                "Find out why the account was stopped before promising them anything. If it has "
                + "been settled, unblock {CustomerNo}.",
                "تعرّف على سبب إيقاف الحساب قبل الالتزام بأي شيء تجاهه. وإن تمت التسوية، "
                + "فألغِ حظر {CustomerNo}."),
            OverridePermission = "Sales.Order.Override",
            HelpTopic = "sales/customers",
        },
        new()
        {
            Code = ItemNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such item", "لا يوجد صنف بهذا الرقم"),
            Detail = new LocalizedText(
                "Line {LineNo} names {ItemNo}, and nothing in this company carries that number.",
                "السطر {LineNo} يشير إلى {ItemNo}، ولا يوجد صنف بهذا الرقم في هذه الشركة."),
            Resolution = new LocalizedText(
                "Check the number against the item list, or create {ItemNo} first.",
                "تحقق من الرقم في قائمة الأصناف، أو أنشئ {ItemNo} أولاً."),
        },
        new()
        {
            Code = QuantityZero,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A line has no quantity", "سطر بدون كمية"),
            Detail = new LocalizedText(
                "Line {LineNo} would sell nothing.",
                "السطر {LineNo} لن يبيع أي كمية."),
            Resolution = new LocalizedText(
                "Enter a quantity on line {LineNo}, or remove the line.",
                "أدخل كمية في السطر {LineNo}، أو احذف السطر."),
        },
        new()
        {
            Code = NoLocation,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nowhere to ship from", "لا يوجد موقع للشحن منه"),
            Detail = new LocalizedText(
                "Line {LineNo} sells stock and names no location, and the order does not name one "
                + "either.",
                "السطر {LineNo} يبيع مخزونًا ولا يحدد موقعًا، ولا يحدد أمر البيع موقعًا كذلك."),
            Resolution = new LocalizedText(
                "Set a location on the order, or on line {LineNo} if it ships from somewhere of "
                + "its own.",
                "حدد موقعًا على أمر البيع، أو على السطر {LineNo} إن كان يُشحن من موقع خاص."),
        },
        new()
        {
            Code = BelowCost,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Selling below cost", "البيع بأقل من التكلفة"),
            Detail = new LocalizedText(
                "{ItemNo} is priced at {NetPrice:N2} on line {LineNo} and costs {UnitCost:N2}, "
                + "a loss of {Shortfall:N2} on every unit.",
                "سعر {ItemNo} في السطر {LineNo} هو {NetPrice:N2} وتكلفته {UnitCost:N2}، "
                + "أي بخسارة {Shortfall:N2} على كل وحدة."),
            Resolution = new LocalizedText(
                "Sometimes this is deliberate — clearing old stock, or a loss leader. The order "
                + "goes through either way; this is here so it is a decision rather than an "
                + "accident nobody sees until the margin report.",
                "أحيانًا يكون هذا مقصودًا، كتصفية مخزون قديم أو عرض جذب. ويمر الأمر في الحالتين، "
                + "والغرض من هذه الرسالة أن يكون قرارًا واعيًا لا خطأً لا يُكتشف إلا في تقرير هامش الربح."),
            HelpTopic = "sales/pricing",
        },
        new()
        {
            Code = NothingToShip,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing left to ship", "لا يوجد ما يُشحن"),
            Detail = new LocalizedText(
                "Everything on {OrderNo} has already gone out.",
                "تم شحن جميع أصناف {OrderNo} بالفعل."),
            Resolution = new LocalizedText(
                "If the customer wants more, take a new order for it so the extra goods have a "
                + "price they agreed to.",
                "إن أراد العميل المزيد، فسجّل أمر بيع جديدًا ليكون للبضاعة الإضافية سعر متفق عليه."),
        },
        new()
        {
            Code = OverShipment,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("More than was ordered", "أكثر من الكمية المطلوبة"),
            Detail = new LocalizedText(
                "{Shipped:0.#####} of {ItemNo} was entered against line {LineNo}, which has "
                + "{OutstandingQuantity:0.#####} still to go of {Ordered:0.#####} ordered.",
                "تم إدخال {Shipped:0.#####} من {ItemNo} على السطر {LineNo}، والمتبقي للشحن "
                + "{OutstandingQuantity:0.#####} من أصل {Ordered:0.#####} مطلوبة."),
            Resolution = new LocalizedText(
                "Ship what the order covers and take a separate order for the rest, so the "
                + "customer is charged a price they agreed to.",
                "اشحن ما يغطيه الأمر وسجّل أمرًا منفصلاً للباقي، ليُحاسَب العميل بسعر وافق عليه."),
            OverridePermission = "Sales.Order.Override",
            HelpTopic = "sales/shipments",
        },
        new()
        {
            Code = NothingToInvoice,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing to invoice", "لا يوجد ما يُفوتر"),
            Detail = new LocalizedText(
                "Everything shipped on {OrderNo} has already been invoiced.",
                "تمت فوترة كل ما شُحن على {OrderNo}."),
            Resolution = new LocalizedText(
                "Ship the goods first. Invoicing before despatch is a prepayment request, and "
                + "belongs on its own document.",
                "اشحن البضاعة أولاً. فالفوترة قبل الشحن تُعد طلب دفعة مقدمة ولها مستند خاص."),
        },
        new()
        {
            Code = InvoiceExceedsShipment,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Invoicing more than was shipped", "الفوترة أكثر مما شُحن"),
            Detail = new LocalizedText(
                "The invoice covers {Invoiced:0.#####} of {ItemNo} on line {LineNo}, and only "
                + "{OutstandingQuantity:0.#####} shipped has yet to be invoiced.",
                "تغطي الفاتورة {Invoiced:0.#####} من {ItemNo} في السطر {LineNo}، ولم يتبق دون "
                + "فوترة سوى {OutstandingQuantity:0.#####} مما شُحن."),
            Resolution = new LocalizedText(
                "Billing for goods the customer has not received is how a dispute starts. Ship "
                + "the rest first, or invoice only what has gone.",
                "فوترة بضاعة لم يستلمها العميل هي بداية أي نزاع. اشحن الباقي أولاً، أو افوتر "
                + "ما شُحن فقط."),
            OverridePermission = "Sales.Order.Override",
            HelpTopic = "sales/invoices",
        },
        new()
        {
            Code = NoRevenueAccount,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("No revenue account", "لا يوجد حساب إيرادات"),
            Detail = new LocalizedText(
                "An invoice has to credit revenue somewhere, and no account has been set for it.",
                "يجب أن تُرحّل الفاتورة الإيراد إلى حساب، ولم يُحدد أي حساب لذلك."),
            Resolution = new LocalizedText(
                "Set the revenue account in Sales setup.",
                "حدد حساب الإيرادات في إعدادات المبيعات."),
            HelpTopic = "sales/setup",
        },
    ];
}
