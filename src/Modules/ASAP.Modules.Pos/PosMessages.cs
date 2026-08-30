using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Pos;

/// <summary>
/// Everything the till can refuse, and why.
/// </summary>
/// <remarks>
/// A till is read by somebody with a queue behind them. Every message here has to be answerable
/// in one action at the counter, or it is not a message, it is an interruption. Where the answer
/// is genuinely a supervisor's, the message says so and names the permission rather than leaving
/// a cashier to guess who to fetch.
/// </remarks>
public static class PosMessages
{
    /// <summary>A device names a till the company does not have.</summary>
    public static readonly MessageCode DeviceStationNotFound = new("POS.DEVICE.STATION_NOT_FOUND");

    /// <summary>No device by that code at that till.</summary>
    public static readonly MessageCode DeviceNotFound = new("POS.DEVICE.NOT_FOUND");

    /// <summary>A device not reached through the browser has to say where it is.</summary>
    public static readonly MessageCode DeviceNeedsAddress = new("POS.DEVICE.NEEDS_ADDRESS");

    /// <summary>Another device of the same kind stopped being the default.</summary>
    public static readonly MessageCode DeviceDefaultMoved = new("POS.DEVICE.DEFAULT_MOVED");

    /// <summary>The station named does not exist.</summary>
    public static readonly MessageCode StationNotFound = new("POS.STATION.NOT_FOUND");

    /// <summary>The station is out of service.</summary>
    public static readonly MessageCode StationBlocked = new("POS.STATION.BLOCKED");

    /// <summary>A session was opened at a till that already had one open.</summary>
    public static readonly MessageCode SessionAlreadyOpen = new("POS.SESSION.ALREADY_OPEN");

    /// <summary>The session named does not exist.</summary>
    public static readonly MessageCode SessionNotFound = new("POS.SESSION.NOT_FOUND");

    /// <summary>Something tried to trade against a session that has been counted and closed.</summary>
    public static readonly MessageCode SessionClosed = new("POS.SESSION.CLOSED");

    /// <summary>A session was closed with receipts still parked at the till.</summary>
    public static readonly MessageCode ParkedSalesRemain = new("POS.SESSION.PARKED_REMAIN");

    /// <summary>The drawer was counted and the count does not agree with what was taken.</summary>
    public static readonly MessageCode CashVariance = new("POS.SESSION.CASH_VARIANCE");

    /// <summary>The receipt named does not exist.</summary>
    public static readonly MessageCode ReceiptNotFound = new("POS.RECEIPT.NOT_FOUND");

    /// <summary>Nothing is set up to print this kind of document.</summary>
    public static readonly MessageCode NoPrintTemplate = new("POS.PRINT.NO_TEMPLATE");

    /// <summary>Something tried to change a receipt that has already posted.</summary>
    public static readonly MessageCode ReceiptNotEditable = new("POS.RECEIPT.NOT_EDITABLE");

    /// <summary>A receipt was rung up with nothing on it.</summary>
    public static readonly MessageCode ReceiptHasNoLines = new("POS.RECEIPT.NO_LINES");

    /// <summary>Less was handed over than the receipt comes to.</summary>
    public static readonly MessageCode Underpaid = new("POS.TENDER.UNDERPAID");

    /// <summary>What was handed back on a refund does not match what is owed.</summary>
    public static readonly MessageCode RefundMismatch = new("POS.TENDER.REFUND_MISMATCH");

    /// <summary>Change was owed on something that cannot give change.</summary>
    public static readonly MessageCode NoChangeFromTender = new("POS.TENDER.NO_CHANGE");

    /// <summary>A line was rung up with no quantity.</summary>
    public static readonly MessageCode QuantityZero = new("POS.LINE.QUANTITY_ZERO");

    /// <summary>The item scanned is not in the catalogue.</summary>
    public static readonly MessageCode ItemNotFound = new("POS.LINE.ITEM_NOT_FOUND");

    /// <summary>The till sells from a location that tracks bins and has no shelf set.</summary>
    public static readonly MessageCode TillHasNoPickBin = new("POS.STATION.NO_PICK_BIN");

    /// <summary>A line would sell below what the goods cost.</summary>
    public static readonly MessageCode BelowCost = new("POS.LINE.BELOW_COST");

    /// <summary>A discount larger than the till is allowed to give without asking.</summary>
    public static readonly MessageCode DiscountAboveLimit = new("POS.LINE.DISCOUNT_ABOVE_LIMIT");

    /// <summary>A cash sale was charged to an account instead of being paid for.</summary>
    public static readonly MessageCode OnAccountNeedsCustomer = new("POS.TENDER.NEEDS_CUSTOMER");

    /// <summary>Something tried to recall or void a receipt that is not parked.</summary>
    public static readonly MessageCode ReceiptNotParked = new("POS.RECEIPT.NOT_PARKED");

    /// <summary>A return asked for more than was bought on the receipt it names.</summary>
    public static readonly MessageCode ReturnExceedsSale = new("POS.RETURN.EXCEEDS_SALE");

    /// <summary>A return names a receipt that was itself a return.</summary>
    public static readonly MessageCode ReturnAgainstReturn = new("POS.RETURN.AGAINST_RETURN");

    /// <summary>No account is set up for a tender that needs one.</summary>
    public static readonly MessageCode NoTenderAccount = new("POS.SETUP.NO_TENDER_ACCOUNT");

    /// <summary>No account is set up for the difference a count leaves behind.</summary>
    public static readonly MessageCode NoVarianceAccount = new("POS.SETUP.NO_VARIANCE_ACCOUNT");

    /// <summary>Every message the module can raise.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = DeviceStationNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such till", "لا توجد نقطة بيع بهذا الرمز"),
            Detail = new LocalizedText(
                "Nothing in this company is set up as till {Station}.",
                "لا يوجد في هذه الشركة نقطة بيع بالرمز {Station}."),
            Resolution = new LocalizedText(
                "Choose a till from the list, or add {Station} first. A device belongs to a till, "
                + "which is what lets a broken one be swapped without anybody reconfiguring "
                + "anything.",
                "اختر نقطة بيع من القائمة، أو أضف {Station} أولًا. فالجهاز يتبع نقطة بيع، وهذا ما "
                + "يتيح استبدال جهاز معطوب دون إعادة ضبط أي شيء."),
            HelpTopic = "pos/devices",
        },
        new()
        {
            Code = DeviceNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such device", "لا يوجد جهاز بهذا الرمز"),
            Detail = new LocalizedText(
                "Till {Station} has no device {Device}.",
                "لا يوجد بنقطة البيع {Station} جهاز بالرمز {Device}."),
            Resolution = new LocalizedText(
                "Choose one from the till's own list of devices.",
                "اختر واحدًا من قائمة أجهزة نقطة البيع نفسها."),
            HelpTopic = "pos/devices",
        },
        new()
        {
            Code = DeviceNeedsAddress,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "This device has to say where it is", "يجب تحديد موضع هذا الجهاز"),
            Detail = new LocalizedText(
                "{Device} is reached over {Connection}, and nothing says where to find it.",
                "يُوصل إلى {Device} عبر {Connection}، ولا شيء يحدد أين يوجد."),
            Resolution = new LocalizedText(
                "Give it an address: a host and port for a network device, a port name for a "
                + "wired one. A device reached through the browser needs none, because the print "
                + "dialog asks the person standing at the till — which is the right place for "
                + "that question and the reason a receipt printer needs no setup at all.",
                "حدّد له عنوانًا: مضيفًا ومنفذًا للجهاز الشبكي، واسم منفذ للجهاز السلكي. أما "
                + "الجهاز الذي يُوصل إليه عبر المتصفح فلا يحتاج شيئًا، لأن نافذة الطباعة تسأل من "
                + "يقف عند نقطة البيع، وهذا هو الموضع الصحيح لذلك السؤال والسبب في أن طابعة "
                + "الإيصالات لا تحتاج أي إعداد."),
            HelpTopic = "pos/devices",
        },
        new()
        {
            Code = DeviceDefaultMoved,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("The default moved", "تغيّر الجهاز الافتراضي"),
            Detail = new LocalizedText(
                "{Device} is no longer the default {Kind} for this till.",
                "لم يعد {Device} هو الجهاز الافتراضي من نوع {Kind} لنقطة البيع هذه."),
            Resolution = new LocalizedText(
                "Nothing to do. A till may have two of a kind — a counter printer and a kitchen "
                + "printer — but only one of them can be the one meant when nothing says.",
                "لا حاجة لأي إجراء. فقد يكون بنقطة البيع جهازان من نوع واحد، كطابعة للكاونتر "
                + "وأخرى للمطبخ، لكن واحدًا فقط يكون المقصود حين لا يُحدد شيء."),
            HelpTopic = "pos/devices",
        },
        new()
        {
            Code = StationNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such till", "لا توجد نقطة بيع بهذا الرمز"),
            Detail = new LocalizedText(
                "No till in this company is coded {StationCode}.",
                "لا توجد في هذه الشركة نقطة بيع تحمل الرمز {StationCode}."),
            Resolution = new LocalizedText(
                "Check the code against the till list, or create {StationCode} first.",
                "تحقق من الرمز في قائمة نقاط البيع، أو أنشئ {StationCode} أولاً."),
            HelpTopic = "pos/stations",
        },
        new()
        {
            Code = StationBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This till is out of service", "نقطة البيع متوقفة عن الخدمة"),
            Detail = new LocalizedText(
                "{StationCode} {StationName} has been blocked, so no session may be opened on it.",
                "تم إيقاف نقطة البيع {StationCode} {StationName}، فلا يمكن فتح وردية عليها."),
            Resolution = new LocalizedText(
                "Trade at another till, or have somebody who maintains the till list unblock this one.",
                "استخدم نقطة بيع أخرى، أو اطلب ممن يدير قائمة نقاط البيع إعادة تفعيل هذه."),
            HelpTopic = "pos/stations",
        },
        new()
        {
            Code = SessionAlreadyOpen,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This till is already open", "الوردية مفتوحة بالفعل"),
            Detail = new LocalizedText(
                "{StationCode} has session {SessionNo} open, started by {CashierName}. One drawer "
                + "cannot be counted twice, so it cannot be worked by two sessions at once.",
                "نقطة البيع {StationCode} لديها الوردية {SessionNo} مفتوحة، بدأها {CashierName}. "
                + "ولا يمكن جرد الدرج مرتين، فلا يجوز تشغيله بورديتين في آن واحد."),
            Resolution = new LocalizedText(
                "Close {SessionNo} with a cash count first, then open a new one.",
                "أغلق الوردية {SessionNo} بجرد النقد أولاً، ثم افتح وردية جديدة."),
            HelpTopic = "pos/sessions",
        },
        new()
        {
            Code = SessionNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such session", "لا توجد وردية بهذا الرقم"),
            Detail = new LocalizedText(
                "No till session in this company is numbered {SessionNo}.",
                "لا توجد في هذه الشركة وردية تحمل الرقم {SessionNo}."),
            Resolution = new LocalizedText(
                "Check the number against the session list.",
                "تحقق من الرقم في قائمة الورديات."),
            HelpTopic = "pos/sessions",
        },
        new()
        {
            Code = SessionClosed,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This session has been counted", "تم جرد هذه الوردية"),
            Detail = new LocalizedText(
                "Session {SessionNo} was closed on {ClosedAt:d} and its drawer counted. A sale added "
                + "now would not be in the count, so the till would be short by exactly this "
                + "amount and nobody would know why.",
                "أُغلقت الوردية {SessionNo} بتاريخ {ClosedAt:d} وجُرد درجها. وأي عملية تُضاف الآن لن "
                + "تكون ضمن الجرد، فيظهر عجز في الصندوق بمقدارها دون سبب ظاهر."),
            Resolution = new LocalizedText(
                "Open a new session and ring the sale up on that.",
                "افتح وردية جديدة وسجّل العملية عليها."),
            HelpTopic = "pos/sessions",
        },
        new()
        {
            Code = ParkedSalesRemain,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Sales are still parked", "توجد عمليات معلّقة"),
            Detail = new LocalizedText(
                "{ParkedCount} sale(s) are parked at {StationCode} and have not been paid for. "
                + "Closing now would leave them where the next cashier will find them attached to "
                + "a session that has already been counted.",
                "توجد {ParkedCount} عملية معلّقة على {StationCode} ولم تُدفع بعد. والإغلاق الآن "
                + "يتركها للكاشير التالي مرتبطة بوردية تم جردها."),
            Resolution = new LocalizedText(
                "Recall each parked sale and either take payment or void it, then close.",
                "استرجع كل عملية معلّقة واستوفِ قيمتها أو ألغِها، ثم أغلق الوردية."),
            OverridePermission = "Pos.Session.Override",
            HelpTopic = "pos/sessions",
        },
        new()
        {
            Code = CashVariance,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("The drawer does not agree", "الدرج لا يطابق"),
            Detail = new LocalizedText(
                "{DeclaredCash:N2} was counted and {ExpectedCash:N2} was expected, a difference of "
                + "{Variance:N2}. The float was {OpeningFloat:N2}, cash taken {CashTendered:N2} and change "
                + "given {ChangeGiven:N2}.",
                "تم جرد {DeclaredCash:N2} والمتوقع {ExpectedCash:N2}، بفارق {Variance:N2}. العهدة "
                + "{OpeningFloat:N2}، والنقد المستلم {CashTendered:N2}، والمتبقي المُعاد {ChangeGiven:N2}."),
            Resolution = new LocalizedText(
                "The difference has been posted so the cash account matches the drawer. Count "
                + "again before accepting it if the amount is large.",
                "رُحّل الفارق ليتطابق حساب النقدية مع الدرج. أعد الجرد قبل اعتماده إن كان المبلغ كبيرًا."),
            HelpTopic = "pos/sessions",
        },
        new()
        {
            Code = NoPrintTemplate,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing to print it with", "لا يوجد قالب للطباعة"),
            Detail = new LocalizedText(
                "No active {Kind} template is set up, for this branch or for the company.",
                "لا يوجد قالب نشط من نوع {Kind} لا لهذا الفرع ولا للشركة."),
            Resolution = new LocalizedText(
                "Add one on the print templates screen. A branch's own template is used where it "
                + "has one, and the company's where it does not.",
                "أضف قالبًا في شاشة قوالب الطباعة. ويُستخدم قالب الفرع إن وُجد، وإلا فقالب الشركة."),
            HelpTopic = "pos/printing",
        },
        new()
        {
            Code = ReceiptNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such receipt", "لا يوجد إيصال بهذا الرقم"),
            Detail = new LocalizedText(
                "No receipt in this company is numbered {ReceiptNo}.",
                "لا يوجد في هذه الشركة إيصال يحمل الرقم {ReceiptNo}."),
            Resolution = new LocalizedText(
                "Check the number printed on the receipt.",
                "تحقق من الرقم المطبوع على الإيصال."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = ReceiptNotEditable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This receipt has been paid", "تم دفع هذا الإيصال"),
            Detail = new LocalizedText(
                "Receipt {ReceiptNo} posted as transaction {TransactionNo}. The goods have gone "
                + "and the money is accounted for, and changing it now would restate both.",
                "رُحّل الإيصال {ReceiptNo} ضمن الحركة {TransactionNo}. خرجت البضاعة وسُجّل المبلغ، "
                + "وتعديله الآن يعيد صياغة الاثنين."),
            Resolution = new LocalizedText(
                "Ring up a return against {ReceiptNo} instead, which leaves both records standing.",
                "سجّل مرتجعًا على الإيصال {ReceiptNo} بدلاً من ذلك، فيبقى السجلان قائمين."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = ReceiptHasNoLines,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing has been scanned", "لم يُسجَّل أي صنف"),
            Detail = new LocalizedText(
                "A receipt was sent for payment with nothing on it.",
                "أُرسل إيصال للدفع دون أي أصناف."),
            Resolution = new LocalizedText(
                "Scan at least one item before taking payment.",
                "امسح صنفًا واحدًا على الأقل قبل استيفاء الدفع."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = Underpaid,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Not enough has been paid", "المبلغ المدفوع غير كافٍ"),
            Detail = new LocalizedText(
                "The receipt comes to {TotalAmount:N2} and {TenderedAmount:N2} was put towards it, "
                + "leaving {OutstandingAmount:N2}.",
                "قيمة الإيصال {TotalAmount:N2} وقُدّم {TenderedAmount:N2}، فيتبقى {OutstandingAmount:N2}."),
            Resolution = new LocalizedText(
                "Take the remaining {OutstandingAmount:N2}, or remove a line.",
                "استوفِ المتبقي {OutstandingAmount:N2}، أو احذف أحد السطور."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = RefundMismatch,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText(
                "The refund does not add up",
                "المبلغ المُعاد لا يطابق"),
            Detail = new LocalizedText(
                "{TotalAmount:N2} is owed back and {TenderedAmount:N2} was handed over, a "
                + "difference of {DifferenceAmount:N2}. A refund is paid out exactly; there is no "
                + "change to give on money going the other way.",
                "المستحق إعادته {TotalAmount:N2} والمُسلَّم {TenderedAmount:N2}، بفارق "
                + "{DifferenceAmount:N2}. والمرتجع يُدفع بالضبط، فلا باقي على مبلغ يخرج من الصندوق."),
            Resolution = new LocalizedText(
                "Hand back exactly {TotalAmount:N2}, or change what is being returned.",
                "أعد {TotalAmount:N2} بالضبط، أو عدّل ما يجري إرجاعه."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = NoChangeFromTender,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("This cannot give change", "لا يمكن إعادة الباقي من هذه الوسيلة"),
            Detail = new LocalizedText(
                "{TenderKind} was offered for {TenderedAmount:N2} against a receipt of {TotalAmount:N2}, "
                + "which would leave {ChangeGiven:N2} to hand back. Only cash can give change; a card "
                + "is charged for what is owed and no more.",
                "قُدّمت {TenderKind} بمبلغ {TenderedAmount:N2} مقابل إيصال قيمته {TotalAmount:N2}، مما "
                + "يستلزم إعادة {ChangeGiven:N2}. والنقد وحده يُعاد منه الباقي، أما البطاقة فتُخصم "
                + "بالمستحق فقط."),
            Resolution = new LocalizedText(
                "Charge {TenderKind} the exact amount, and take any excess in cash if there is one.",
                "اخصم من {TenderKind} المبلغ المستحق تمامًا، واستوفِ أي زيادة نقدًا إن وُجدت."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = QuantityZero,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The line has no quantity", "السطر بدون كمية"),
            Detail = new LocalizedText(
                "Line {LineNo} for {ItemNo} was rung up with a quantity of zero.",
                "السطر {LineNo} للصنف {ItemNo} سُجّل بكمية صفر."),
            Resolution = new LocalizedText(
                "Key a quantity, or remove the line.",
                "أدخل كمية، أو احذف السطر."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = TillHasNoPickBin,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This till has no shelf set", "لم يُحدَّد رف لهذه النقطة"),
            Detail = new LocalizedText(
                "{StationCode} sells from {Location}, which tracks stock down to the bin, and "
                + "nobody has said which bin the shop floor is.",
                "تبيع النقطة {StationCode} من الموقع {Location} الذي يتتبع المخزون حتى الرف، ولم "
                + "يحدد أحد أي رف هو أرض المتجر."),
            Resolution = new LocalizedText(
                "Set the till's shelf in station setup. It is asked once there rather than on "
                + "every sale, because a cashier took the goods off the shop floor and has no way "
                + "to say where that is on a warehouse map.",
                "حدّد رف النقطة في إعداد النقاط. ويُسأل عنه مرة واحدة هناك لا في كل بيعة، لأن "
                + "الكاشير أخذ البضاعة من أرض المتجر ولا سبيل له إلى بيان موضعها على خريطة "
                + "المستودع."),
            HelpTopic = "pos/stations",
        },
        new()
        {
            Code = ItemNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("This item is not in the catalogue", "الصنف غير موجود"),
            Detail = new LocalizedText(
                "Nothing in this company is coded {ItemNo}.",
                "لا يوجد في هذه الشركة صنف يحمل الرمز {ItemNo}."),
            Resolution = new LocalizedText(
                "Check the barcode, or key the item number by hand.",
                "تحقق من الباركود، أو أدخل رقم الصنف يدويًا."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = BelowCost,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("This sells below cost", "البيع بأقل من التكلفة"),
            Detail = new LocalizedText(
                "{ItemNo} {Description} is going for {NetUnitPrice:N2} and cost {UnitCost:N2}, a loss of "
                + "{LossPerUnit:N2} on each of {Quantity:0.#####}.",
                "الصنف {ItemNo} {Description} يُباع بـ {NetUnitPrice:N2} وتكلفته {UnitCost:N2}، بخسارة "
                + "{LossPerUnit:N2} على كل وحدة من {Quantity:0.#####}."),
            Resolution = new LocalizedText(
                "Sell it anyway if that is the decision. It is said here so it is a decision "
                + "rather than something found in a margin report next month.",
                "أتمم البيع إن كان ذلك هو القرار. ويُذكر هنا ليكون قرارًا لا اكتشافًا في تقرير "
                + "هوامش الشهر القادم."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = DiscountAboveLimit,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That discount needs approval", "الخصم يحتاج موافقة"),
            Detail = new LocalizedText(
                "{DiscountPercent:N2}% was keyed on {ItemNo} and this till may give {DiscountLimit:N2}% "
                + "without asking.",
                "أُدخل خصم {DiscountPercent:N2}% على الصنف {ItemNo}، والحد المسموح لهذه النقطة دون "
                + "موافقة هو {DiscountLimit:N2}%."),
            Resolution = new LocalizedText(
                "Reduce it to {DiscountLimit:N2}%, or have a supervisor holding Pos.Receipt.Override "
                + "approve it at the till.",
                "خفّضه إلى {DiscountLimit:N2}%، أو اطلب اعتماده من مشرف يملك صلاحية "
                + "Pos.Receipt.Override عند نقطة البيع."),
            OverridePermission = "Pos.Receipt.Override",
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = OnAccountNeedsCustomer,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nobody to charge this to", "لا يوجد عميل لتحميل المبلغ عليه"),
            Detail = new LocalizedText(
                "{Amount:N2} was put on account, but the receipt is against {CustomerNo}, which is "
                + "the walk-in customer this till records cash sales to. A debt owed by everybody "
                + "is owed by nobody.",
                "حُمّل مبلغ {Amount:N2} على الحساب، لكن الإيصال مسجل على {CustomerNo}، وهو عميل "
                + "المبيعات النقدية العابرة لهذه النقطة. والدين المستحق على الجميع لا يستحقه أحد."),
            Resolution = new LocalizedText(
                "Look the customer up and put the receipt on their account, or take payment now.",
                "ابحث عن العميل وسجّل الإيصال على حسابه، أو استوفِ المبلغ الآن."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = ReceiptNotParked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That sale is not waiting", "هذه العملية ليست معلّقة"),
            Detail = new LocalizedText(
                "Receipt {ReceiptNo} is {Status}, and only a parked sale can be recalled or "
                + "thrown away.",
                "حالة الإيصال {ReceiptNo} هي {Status}، ولا يمكن استرجاع أو إلغاء إلا عملية معلّقة."),
            Resolution = new LocalizedText(
                "Check the list of parked sales at this till.",
                "راجع قائمة العمليات المعلّقة على هذه النقطة."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = ReturnExceedsSale,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("More than was bought", "أكثر مما تم شراؤه"),
            Detail = new LocalizedText(
                "{ReturnQuantity:0.#####} of {ItemNo} is being returned against {ReceiptNo}, which "
                + "sold {SoldQuantity:0.#####} and has already had {ReturnedQuantity:0.#####} back.",
                "يجري إرجاع {ReturnQuantity:0.#####} من الصنف {ItemNo} على الإيصال {ReceiptNo}، "
                + "الذي باع {SoldQuantity:0.#####} وأُرجع منه {ReturnedQuantity:0.#####}."),
            Resolution = new LocalizedText(
                "Take back at most {RemainingQuantity:0.#####}, or ring the return up without "
                + "naming a receipt if the customer bought it another day.",
                "استلم {RemainingQuantity:0.#####} كحد أقصى، أو سجّل المرتجع دون الإشارة إلى إيصال "
                + "إن كان الشراء في يوم آخر."),
            OverridePermission = "Pos.Receipt.Override",
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = ReturnAgainstReturn,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That was itself a return", "هذا الإيصال مرتجع أصلاً"),
            Detail = new LocalizedText(
                "Receipt {ReceiptNo} took goods back rather than selling them, so there is "
                + "nothing on it to return.",
                "الإيصال {ReceiptNo} استلم بضاعة ولم يبعها، فلا يوجد فيه ما يُرجَع."),
            Resolution = new LocalizedText(
                "Name the receipt the goods were originally sold on.",
                "أشر إلى الإيصال الذي بيعت فيه البضاعة أصلاً."),
            HelpTopic = "pos/receipts",
        },
        new()
        {
            Code = NoTenderAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nowhere to post this money", "لا يوجد حساب لترحيل هذا المبلغ"),
            Detail = new LocalizedText(
                "No account is set up for {TenderKind}, so {Amount:N2} taken at the till has nowhere "
                + "to go.",
                "لا يوجد حساب معدّ لوسيلة الدفع {TenderKind}، فمبلغ {Amount:N2} المستلم بلا وجهة."),
            Resolution = new LocalizedText(
                "Set Pos.Posting.{TenderKind}Account in setup to the account that holds it.",
                "حدّد الإعداد Pos.Posting.{TenderKind}Account على الحساب الذي يحتفظ بالمبلغ."),
            HelpTopic = "pos/setup",
        },
        new()
        {
            Code = NoVarianceAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText(
                "Nowhere to post the difference",
                "لا يوجد حساب لترحيل الفارق"),
            Detail = new LocalizedText(
                "The drawer is out by {Variance:N2} and no till variance account is set up, so the "
                + "cash account cannot be made to agree with what was counted.",
                "الدرج به فارق {Variance:N2} ولا يوجد حساب فروقات نقاط البيع، فلا يمكن مطابقة حساب "
                + "النقدية مع ما تم جرده."),
            Resolution = new LocalizedText(
                "Set Pos.Posting.VarianceAccount in setup before closing a session.",
                "حدّد الإعداد Pos.Posting.VarianceAccount قبل إغلاق أي وردية."),
            HelpTopic = "pos/setup",
        },
    ];
}
