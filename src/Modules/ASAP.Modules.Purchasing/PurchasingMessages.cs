using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Purchasing;

/// <summary>Everything the Purchasing module can tell the user.</summary>
public static class PurchasingMessages
{
    /// <summary>The order named does not exist.</summary>
    public static readonly MessageCode OrderNotFound = new("PUR.ORDER.NOT_FOUND");

    /// <summary>An order was submitted with nothing on it.</summary>
    public static readonly MessageCode OrderHasNoLines = new("PUR.ORDER.NO_LINES");

    /// <summary>The vendor named does not exist.</summary>
    public static readonly MessageCode VendorNotFound = new("PUR.ORDER.VENDOR_NOT_FOUND");

    /// <summary>The vendor has been withdrawn from use.</summary>
    public static readonly MessageCode VendorBlocked = new("PUR.ORDER.VENDOR_BLOCKED");

    /// <summary>A line names an item that does not exist.</summary>
    public static readonly MessageCode ItemNotFound = new("PUR.ORDER.ITEM_NOT_FOUND");

    /// <summary>A line carries no quantity.</summary>
    public static readonly MessageCode QuantityZero = new("PUR.ORDER.QUANTITY_ZERO");

    /// <summary>Something tried to change an order that goods have arrived against.</summary>
    public static readonly MessageCode OrderNotEditable = new("PUR.ORDER.NOT_EDITABLE");

    /// <summary>There is nothing left on the order to receive.</summary>
    public static readonly MessageCode NothingToReceive = new("PUR.RECEIPT.NOTHING_OUTSTANDING");

    /// <summary>A receipt names more than was ordered.</summary>
    public static readonly MessageCode OverReceipt = new("PUR.RECEIPT.MORE_THAN_ORDERED");

    /// <summary>There is nothing received that still needs invoicing.</summary>
    public static readonly MessageCode NothingToInvoice = new("PUR.INVOICE.NOTHING_OUTSTANDING");

    /// <summary>An invoice names more than has been received.</summary>
    public static readonly MessageCode InvoiceExceedsReceipt = new("PUR.INVOICE.MORE_THAN_RECEIVED");

    /// <summary>The invoice price differs from the price agreed on the order.</summary>
    public static readonly MessageCode PriceVariance = new("PUR.INVOICE.PRICE_VARIANCE");

    /// <summary>An order line names no destination for its goods.</summary>
    public static readonly MessageCode NoLocation = new("PUR.ORDER.NO_LOCATION");

    /// <summary>The goods-received-not-invoiced account has not been set up.</summary>
    public static readonly MessageCode NoAccrualAccount = new("PUR.SETUP.NO_ACCRUAL_ACCOUNT");

    /// <summary>Every message the module declares.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = OrderNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such purchase order", "لا يوجد أمر شراء بهذا الرقم"),
            Detail = new LocalizedText(
                "Nothing in this company is numbered {OrderNo}.",
                "لا يوجد في هذه الشركة أمر شراء يحمل الرقم {OrderNo}."),
            Resolution = new LocalizedText(
                "Choose the order from the list rather than typing its number.",
                "اختر الأمر من القائمة بدلاً من كتابة رقمه."),
        },
        new()
        {
            Code = OrderHasNoLines,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The order has no lines", "أمر الشراء بدون سطور"),
            Detail = new LocalizedText(
                "An order to {VendorNo} was submitted with nothing on it.",
                "تم إرسال أمر شراء إلى {VendorNo} بدون أي أصناف."),
            Resolution = new LocalizedText(
                "Add at least one item or cost, with a quantity and a price.",
                "أضف صنفًا أو تكلفة واحدة على الأقل، مع الكمية والسعر."),
        },
        new()
        {
            Code = VendorNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such vendor", "لا يوجد مورّد بهذا الرقم"),
            Detail = new LocalizedText(
                "No vendor in this company is numbered {VendorNo}.",
                "لا يوجد مورّد في هذه الشركة يحمل الرقم {VendorNo}."),
            Resolution = new LocalizedText(
                "Check the number against the vendor list, or create {VendorNo} first.",
                "تحقق من الرقم في قائمة المورّدين، أو أنشئ {VendorNo} أولاً."),
        },
        new()
        {
            Code = VendorBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That vendor is blocked", "هذا المورّد محظور"),
            Detail = new LocalizedText(
                "{VendorNo} {VendorName} has been withdrawn from use, so no new orders should be "
                + "placed with them.",
                "تم سحب {VendorNo} {VendorName} من الاستخدام، فلا ينبغي إصدار أوامر شراء جديدة له."),
            Resolution = new LocalizedText(
                "Order from a different vendor, or unblock {VendorNo} if trading has resumed. "
                + "Receiving goods already on their way is a different question and is allowed.",
                "اشترِ من مورّد آخر، أو ألغِ حظر {VendorNo} إن استُؤنف التعامل. أما استلام بضاعة "
                + "في الطريق فمسألة أخرى ومسموح بها."),
            OverridePermission = "Purchasing.Order.Override",
            HelpTopic = "purchasing/vendors",
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
                "Line {LineNo} would order nothing.",
                "السطر {LineNo} لن يطلب أي كمية."),
            Resolution = new LocalizedText(
                "Enter a quantity on line {LineNo}, or remove the line.",
                "أدخل كمية في السطر {LineNo}، أو احذف السطر."),
        },
        new()
        {
            Code = NoLocation,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No destination for the goods", "لا يوجد موقع لاستلام البضاعة"),
            Detail = new LocalizedText(
                "Line {LineNo} buys stock and names no location, and the order does not name one "
                + "either.",
                "السطر {LineNo} يشتري مخزونًا ولا يحدد موقعًا، ولا يحدد أمر الشراء موقعًا كذلك."),
            Resolution = new LocalizedText(
                "Set a location on the order, or on line {LineNo} if this delivery goes somewhere "
                + "of its own.",
                "حدد موقعًا على أمر الشراء، أو على السطر {LineNo} إن كانت هذه الشحنة تذهب لموقع خاص."),
        },
        new()
        {
            Code = OrderNotEditable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This order can no longer be changed", "لا يمكن تعديل هذا الأمر"),
            Detail = new LocalizedText(
                "Goods have already been received against {OrderNo}, which is {Status}.",
                "تم استلام بضاعة على {OrderNo}، وحالته الآن {Status}."),
            Resolution = new LocalizedText(
                "What has arrived is a fact rather than a figure the order gets to restate. "
                + "Receive the rest, invoice what has arrived, or cancel the remainder.",
                "ما وصل فعلاً حقيقة لا يمكن لأمر الشراء تغييرها. استلم الباقي، أو سجّل فاتورة "
                + "لما وصل، أو ألغِ المتبقي."),
            HelpTopic = "purchasing/orders",
        },
        new()
        {
            Code = NothingToReceive,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing left to receive", "لا يوجد ما يُستلم"),
            Detail = new LocalizedText(
                "Everything on {OrderNo} has already arrived.",
                "وصلت جميع أصناف {OrderNo} بالفعل."),
            Resolution = new LocalizedText(
                "If more arrived than was ordered, raise a new order for the difference so the "
                + "extra goods have a document behind them.",
                "إن وصل أكثر مما طُلب، فأنشئ أمر شراء جديدًا للفرق ليكون للبضاعة الزائدة مستند."),
        },
        new()
        {
            Code = OverReceipt,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("More than was ordered", "أكثر من الكمية المطلوبة"),
            Detail = new LocalizedText(
                "{Received:0.#####} of {ItemNo} was entered against line {LineNo}, which has "
                + "{Outstanding:0.#####} still outstanding of {Ordered:0.#####} ordered.",
                "تم إدخال {Received:0.#####} من {ItemNo} على السطر {LineNo}، والمتبقي عليه "
                + "{Outstanding:0.#####} من أصل {Ordered:0.#####} مطلوبة."),
            Resolution = new LocalizedText(
                "Receive what the order covers and raise a separate order for the excess, so the "
                + "extra goods have a price somebody agreed to.",
                "استلم ما يغطيه الأمر وأنشئ أمرًا منفصلاً للزيادة، ليكون للبضاعة الزائدة سعر متفق عليه."),
            OverridePermission = "Purchasing.Order.Override",
            HelpTopic = "purchasing/receipts",
        },
        new()
        {
            Code = NothingToInvoice,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing to invoice", "لا يوجد ما يُفوتر"),
            Detail = new LocalizedText(
                "Everything received on {OrderNo} has already been invoiced.",
                "تمت فوترة كل ما استُلم على {OrderNo}."),
            Resolution = new LocalizedText(
                "Receive the goods first. An invoice for goods that have not arrived is a "
                + "prepayment, and belongs on its own document.",
                "استلم البضاعة أولاً. فالفاتورة عن بضاعة لم تصل تُعد دفعة مقدمة ولها مستند خاص."),
        },
        new()
        {
            Code = InvoiceExceedsReceipt,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Invoiced for more than arrived", "الفاتورة أكثر مما وصل"),
            Detail = new LocalizedText(
                "The invoice covers {Invoiced:0.#####} of {ItemNo} on line {LineNo}, and only "
                + "{Outstanding:0.#####} received has yet to be invoiced.",
                "تغطي الفاتورة {Invoiced:0.#####} من {ItemNo} في السطر {LineNo}، ولم يتبق دون فوترة "
                + "سوى {Outstanding:0.#####} مما استُلم."),
            Resolution = new LocalizedText(
                "This is the check that catches being billed for goods that never came. Confirm "
                + "the delivery before paying, and receive the rest if it did arrive.",
                "هذا الفحص هو ما يكشف الفوترة عن بضاعة لم تصل. تأكد من الاستلام قبل السداد، "
                + "واستلم الباقي إن كان قد وصل فعلاً."),
            OverridePermission = "Purchasing.Order.Override",
            HelpTopic = "purchasing/invoices",
        },
        new()
        {
            Code = PriceVariance,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Invoiced at a different price", "الفاتورة بسعر مختلف"),
            Detail = new LocalizedText(
                "{ItemNo} was ordered at {OrderedCost:N2} and invoiced at {InvoicedCost:N2}, a "
                + "difference of {Variance:N2} on line {LineNo}.",
                "تم طلب {ItemNo} بسعر {OrderedCost:N2} وفُوتر بسعر {InvoicedCost:N2}، بفارق "
                + "{Variance:N2} في السطر {LineNo}."),
            Resolution = new LocalizedText(
                "The invoice price is used, because it is what will be paid, and the difference "
                + "reaches the ledger as a purchase variance. Query it with the vendor if it was "
                + "not agreed.",
                "يُعتمد سعر الفاتورة لأنه المبلغ الذي سيُدفع، ويُرحّل الفارق كفرق مشتريات. "
                + "راجع المورّد إن لم يكن هذا السعر متفقًا عليه."),
            HelpTopic = "purchasing/invoices",
        },
        new()
        {
            Code = NoAccrualAccount,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "No account for goods received not invoiced",
                "لا يوجد حساب للبضاعة المستلمة غير المفوترة"),
            Detail = new LocalizedText(
                "Receiving goods records what the company owes for them before the invoice "
                + "arrives, and that needs an account to sit in.",
                "يسجّل استلام البضاعة ما يستحق على الشركة قبل وصول الفاتورة، وهذا يحتاج إلى حساب."),
            Resolution = new LocalizedText(
                "Set the goods-received-not-invoiced account in Purchasing setup. An accrued "
                + "liability account is the usual choice.",
                "حدد حساب البضاعة المستلمة غير المفوترة في إعدادات المشتريات. ويُستخدم عادةً "
                + "حساب التزامات مستحقة."),
            HelpTopic = "purchasing/setup",
        },
    ];
}
