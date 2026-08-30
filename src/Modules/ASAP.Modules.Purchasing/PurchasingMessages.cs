using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Purchasing;

/// <summary>Everything the Purchasing module can tell the user.</summary>
public static class PurchasingMessages
{
    /// <summary>An approval limit was set below nothing.</summary>
    public static readonly MessageCode ApprovalLimitNegative = new("PUR.APPROVAL.LIMIT_NEGATIVE");

    /// <summary>The order is not waiting to be signed for.</summary>
    public static readonly MessageCode OrderNotAwaitingApproval = new("PUR.APPROVAL.NOT_PENDING");

    /// <summary>Somebody tried to approve an order they raised.</summary>
    public static readonly MessageCode CannotApproveYourOwnOrder = new("PUR.APPROVAL.OWN_ORDER");

    /// <summary>The approver may not sign for this much.</summary>
    public static readonly MessageCode ApprovalLimitTooLow = new("PUR.APPROVAL.LIMIT_TOO_LOW");

    /// <summary>An order was turned down with nothing written against it.</summary>
    public static readonly MessageCode RejectionNeedsAReason = new("PUR.APPROVAL.REASON_REQUIRED");

    /// <summary>The order was signed for.</summary>
    public static readonly MessageCode OrderApproved = new("PUR.APPROVAL.APPROVED");

    /// <summary>The order was turned down.</summary>
    public static readonly MessageCode OrderRejected = new("PUR.APPROVAL.REJECTED");

    /// <summary>The order went for approval rather than to the vendor.</summary>
    public static readonly MessageCode OrderSentForApproval = new("PUR.APPROVAL.SENT");

    /// <summary>Nobody in the company can sign for an order this size.</summary>
    public static readonly MessageCode NobodyCanApproveThis = new("PUR.APPROVAL.NOBODY_CAN");

    /// <summary>A landed cost of nothing or less was offered.</summary>
    public static readonly MessageCode LandedCostNotPositive = new("PUR.LANDED.NOT_POSITIVE");

    /// <summary>A landed cost was offered with nothing to post it against.</summary>
    public static readonly MessageCode LandedCostNeedsAnAccount = new("PUR.LANDED.NO_ACCOUNT");

    /// <summary>Nothing has been received against the order.</summary>
    public static readonly MessageCode NothingReceivedToLandCostOn = new("PUR.LANDED.NOTHING_RECEIVED");

    /// <summary>The chosen basis comes to nothing, so nothing can be shared out by it.</summary>
    public static readonly MessageCode NothingToApportionBy = new("PUR.LANDED.NO_BASIS");

    /// <summary>Part of the charge belonged to goods that have already gone.</summary>
    public static readonly MessageCode LandedCostReachedGoodsAlreadySold = new("PUR.LANDED.CORRECTED_SALES");

    /// <summary>The account a landed cost was to post against will not take an entry.</summary>
    public static readonly MessageCode LandedCostAccountUnusable = new("PUR.LANDED.ACCOUNT_UNUSABLE");

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
            Code = ApprovalLimitNegative,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A limit cannot be below nothing", "لا يكون الحد دون الصفر"),
            Detail = new LocalizedText(
                "{Amount:N2} was given as an approval limit.",
                "أُعطي المبلغ {Amount:N2} حدًّا للاعتماد."),
            Resolution = new LocalizedText(
                "Set nought to let somebody approve nothing, or a figure they may sign up to.",
                "اجعله صفرًا ليكون بلا صلاحية اعتماد، أو مبلغًا يجوز له التوقيع حتى حده."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = OrderNotAwaitingApproval,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That order is not waiting for a signature", "هذا الأمر لا ينتظر توقيعًا"),
            Detail = new LocalizedText(
                "{OrderNo} is {Status}.",
                "حالة الأمر {OrderNo} هي {Status}."),
            Resolution = new LocalizedText(
                "Only an order sent for approval can be signed for or turned down.",
                "لا يُوقَّع أو يُرفض إلا أمر أُرسل للاعتماد."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = CannotApproveYourOwnOrder,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("You raised this one", "أنت من أصدر هذا الأمر"),
            Detail = new LocalizedText(
                "{OrderNo} was raised by the same person now approving it.",
                "الأمر {OrderNo} أصدره الشخص نفسه الذي يعتمده الآن."),
            Resolution = new LocalizedText(
                "Somebody else has to sign. An approval you can give yourself is not a control but "
                + "a checkbox, and the whole point of the step is that a second person looked.",
                "على شخص آخر أن يوقّع. فالاعتماد الذي تمنحه لنفسك ليس ضابطًا بل خانة تُعلَّم، "
                + "والغاية كلها من هذه الخطوة أن ينظر فيها شخص ثانٍ."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = ApprovalLimitTooLow,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That is more than you may sign for", "هذا يتجاوز حد توقيعك"),
            Detail = new LocalizedText(
                "{OrderNo} comes to {Amount:N2} and your limit is {Limit:N2}. {WhoCan}",
                "الأمر {OrderNo} يبلغ {Amount:N2} وحدك {Limit:N2}. {WhoCan}"),
            Resolution = new LocalizedText(
                "Take it to somebody whose limit covers it. Somebody with no limit at all approves "
                + "nothing, because a system where unknown means unlimited answers \"who can "
                + "approve this\" with \"whoever has not been set up yet\".",
                "خذه إلى من يغطي حده المبلغ. ومن لا حد له لا يعتمد شيئًا، لأن نظامًا يعني فيه "
                + "المجهول «بلا حدود» يجيب عن سؤال «من يعتمد هذا» بـ«كل من لم يُضبط بعد»."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = RejectionNeedsAReason,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Say why", "بيّن السبب"),
            Detail = new LocalizedText(
                "{OrderNo} was turned down with nothing written against it.",
                "رُفض الأمر {OrderNo} دون كتابة شيء معه."),
            Resolution = new LocalizedText(
                "Write what was wrong. A rejection with no reason sends the buyer back to guess, "
                + "and the order comes round again unchanged.",
                "اكتب ما الخطأ. فالرفض بلا سبب يعيد المشتري إلى التخمين، ويعود الأمر كما هو."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = OrderApproved,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("Approved", "اعتُمد"),
            Detail = new LocalizedText(
                "{OrderNo} was approved for {Amount:N2} by {Approver}.",
                "اعتُمد الأمر {OrderNo} بمبلغ {Amount:N2} من {Approver}."),
            Resolution = new LocalizedText(
                "It is released to the vendor. The amount is recorded with the signature, so "
                + "anything that changes the total has to be approved again.",
                "أُرسل إلى المورّد. والمبلغ مسجّل مع التوقيع، فأي تغيير في الإجمالي يستلزم "
                + "اعتمادًا جديدًا."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = OrderRejected,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("Turned down", "مرفوض"),
            Detail = new LocalizedText(
                "{OrderNo} was rejected: {Reason}",
                "رُفض الأمر {OrderNo}: {Reason}"),
            Resolution = new LocalizedText(
                "It stays on the record with the reason on it, because somebody will ask.",
                "يبقى في السجل ومعه سببه، لأن أحدهم سيسأل."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = LandedCostAccountUnusable,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing will post to that account", "لن يُرحّل شيء إلى هذا الحساب"),
            Detail = new LocalizedText(
                "{AccountNo} was given for the charge on {OrderNo}, and {Reason}.",
                "أُعطي الحساب {AccountNo} لرسم الأمر {OrderNo}، و{Reason}."),
            Resolution = new LocalizedText(
                "Name an account entries can land on. Checked before anything is written rather "
                + "than discovered by the ledger afterwards, because half a landed cost is worse "
                + "than none: the cost layers would carry the charge and the accounts would not.",
                "حدّد حسابًا تقع عليه القيود. ويُفحص قبل كتابة أي شيء لا بعد أن يكتشفه دفتر "
                + "الأستاذ، لأن نصف تكلفة توريد أسوأ من لا شيء: فتحمل طبقات التكلفة الرسم ولا "
                + "تحمله الحسابات."),
            HelpTopic = "purchasing/landed-cost",
        },
        new()
        {
            Code = LandedCostNotPositive,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A charge has to be worth something", "الرسم يجب أن يكون له قيمة"),
            Detail = new LocalizedText(
                "{Amount:N2} was offered as a landed cost.",
                "عُرض المبلغ {Amount:N2} كتكلفة توريد."),
            Resolution = new LocalizedText(
                "Give the amount on the carrier's invoice. A credit note against a charge already "
                + "landed is a different operation and not this one.",
                "أدخل المبلغ في فاتورة الناقل. أما الإشعار الدائن على رسم مُحمّل فعملية أخرى ليست هذه."),
            HelpTopic = "purchasing/landed-cost",
        },
        new()
        {
            Code = LandedCostNeedsAnAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Say what it posts against", "حدّد ما يُرحّل مقابله"),
            Detail = new LocalizedText(
                "A landed cost on {OrderNo} was offered with no account to post against.",
                "عُرضت تكلفة توريد على الأمر {OrderNo} بلا حساب تُرحّل مقابله."),
            Resolution = new LocalizedText(
                "Name the accrual the carrier will be paid from. The charge raises the value of "
                + "goods, and the other side of that has to be somewhere.",
                "حدّد المستحق الذي سيُدفع منه للناقل. فالرسم يرفع قيمة البضاعة، ولا بد للطرف الآخر من مكان."),
            HelpTopic = "purchasing/landed-cost",
        },
        new()
        {
            Code = NothingReceivedToLandCostOn,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Nothing has arrived on that order", "لم يصل شيء على هذا الأمر"),
            Detail = new LocalizedText(
                "{OrderNo} has no goods receipts to spread a charge across.",
                "الأمر {OrderNo} ليس فيه استلامات لتوزيع الرسم عليها."),
            Resolution = new LocalizedText(
                "Receive the goods first. Freight belongs to the goods it carried, and until they "
                + "have arrived there is nothing for it to attach to.",
                "استلم البضاعة أولًا. فالشحن يخص البضاعة التي حملها، وما لم تصل فليس له ما يتعلق به."),
            HelpTopic = "purchasing/landed-cost",
        },
        new()
        {
            Code = NothingToApportionBy,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Nothing to share it out by", "لا شيء يُوزَّع عليه"),
            Detail = new LocalizedText(
                "The receipts on {OrderNo} come to nothing by {Basis}.",
                "استلامات الأمر {OrderNo} مجموعها صفر بحسب {Basis}."),
            Resolution = new LocalizedText(
                "Choose the other basis. Spreading it evenly instead would be inventing a basis "
                + "nobody chose, and the wrong item would carry the charge.",
                "اختر الأساس الآخر. فتوزيعه بالتساوي اختراع لأساس لم يخترْه أحد، ويحمل الرسمَ الصنفُ الخطأ."),
            HelpTopic = "purchasing/landed-cost",
        },
        new()
        {
            Code = LandedCostReachedGoodsAlreadySold,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("Some of it belonged to goods already sold", "جزء منه يخص بضاعة بيعت"),
            Detail = new LocalizedText(
                "{Amount:N2} of the charge on {OrderNo} covered goods that have gone, so it "
                + "corrected their cost of sales rather than the value of stock.",
                "غطّى {Amount:N2} من رسم الأمر {OrderNo} بضاعةً خرجت، فصحّح تكلفة مبيعاتها بدل "
                + "قيمة المخزون."),
            Resolution = new LocalizedText(
                "Nothing to do. A landed cost is what the goods cost all along, so the sales that "
                + "already happened were understating their cost until now.",
                "لا شيء يُفعل. فتكلفة التوريد هي ما كلّفته البضاعة منذ البداية، وكانت المبيعات "
                + "التي تمت تُنقص تكلفتها حتى الآن."),
            HelpTopic = "purchasing/landed-cost",
        },
        new()
        {
            Code = NobodyCanApproveThis,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Nobody can sign for this", "لا أحد يستطيع التوقيع على هذا"),
            Detail = new LocalizedText(
                "{OrderNo} comes to {Amount:N2}, and nobody other than whoever raised it has an "
                + "approval limit that covers it.",
                "يبلغ الأمر {OrderNo} مبلغ {Amount:N2}، ولا أحد غير من أصدره يملك حد اعتماد يغطيه."),
            Resolution = new LocalizedText(
                "Raise somebody's limit, or split the order. It will sit waiting until one of "
                + "those happens -- said now rather than discovered next week, because an order "
                + "nobody can approve looks exactly like one nobody has got to yet.",
                "ارفع حد أحدهم، أو قسّم الأمر. فسيبقى منتظرًا حتى يحدث أحدهما — ويُقال ذلك الآن "
                + "لا يُكتشف الأسبوع المقبل، لأن أمرًا لا يستطيع أحد اعتماده يبدو تمامًا كأمر لم "
                + "يصل إليه أحد بعد."),
            HelpTopic = "purchasing/approval-limits",
        },
        new()
        {
            Code = OrderSentForApproval,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("Sent for approval", "أُرسل للاعتماد"),
            Detail = new LocalizedText(
                "{OrderNo} comes to {Amount:N2}, which is above the {Threshold:N2} this company "
                + "lets through unsigned.",
                "يبلغ الأمر {OrderNo} مبلغ {Amount:N2}، وهو فوق {Threshold:N2} التي تمررها هذه "
                + "الشركة بلا توقيع."),
            Resolution = new LocalizedText(
                "Nothing has gone to the vendor yet. It goes the moment somebody with the "
                + "authority signs for it.",
                "لم يذهب شيء إلى المورّد بعد. ويذهب فور توقيع صاحب الصلاحية عليه."),
            HelpTopic = "purchasing/approval-limits",
        },

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
                + "{OutstandingQuantity:0.#####} still outstanding of {Ordered:0.#####} ordered.",
                "تم إدخال {Received:0.#####} من {ItemNo} على السطر {LineNo}، والمتبقي عليه "
                + "{OutstandingQuantity:0.#####} من أصل {Ordered:0.#####} مطلوبة."),
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
                + "{OutstandingQuantity:0.#####} received has yet to be invoiced.",
                "تغطي الفاتورة {Invoiced:0.#####} من {ItemNo} في السطر {LineNo}، ولم يتبق دون فوترة "
                + "سوى {OutstandingQuantity:0.#####} مما استُلم."),
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
