using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Inventory;

/// <summary>
/// Everything the Inventory module can tell the user.
/// </summary>
public static class InventoryMessages
{
    /// <summary>Nothing in this company carries that barcode.</summary>
    public static readonly MessageCode BarcodeNotFound = new("INV.BARCODE.NOT_FOUND");

    /// <summary>A unit was named for an item that has no such unit set up.</summary>
    public static readonly MessageCode UnitNotSetUpForItem = new("INV.UNIT.NOT_SET_UP");

    /// <summary>A unit's conversion factor cannot be used.</summary>
    public static readonly MessageCode UnitFactorNotUsable = new("INV.UNIT.FACTOR_UNUSABLE");

    /// <summary>No item by that number, asked while converting a unit.</summary>
    public static readonly MessageCode ItemNotFoundForUnit = new("INV.UNIT.ITEM_NOT_FOUND");

    /// <summary>A quantity carries more decimal places than its unit allows.</summary>
    public static readonly MessageCode QuantityTooPrecise = new("INV.UNIT.TOO_MANY_DECIMALS");

    /// <summary>A unit was saved without a code.</summary>
    public static readonly MessageCode UnitCodeRequired = new("INV.UNIT.CODE_REQUIRED");

    /// <summary>A unit asks for more decimal places than a quantity can hold.</summary>
    public static readonly MessageCode DecimalPlacesOutOfRange = new("INV.UNIT.PLACES_OUT_OF_RANGE");

    /// <summary>An item names a unit the company never agreed on.</summary>
    public static readonly MessageCode UnitNotInCompanyList = new("INV.UNIT.NOT_IN_LIST");

    /// <summary>The base unit was given a factor other than one.</summary>
    public static readonly MessageCode BaseUnitFactorMustBeOne = new("INV.UNIT.BASE_NOT_ONE");

    /// <summary>Something else already carries that barcode.</summary>
    public static readonly MessageCode BarcodeAlreadyInUse = new("INV.BARCODE.IN_USE");

    /// <summary>Stock would go below zero and the company does not permit it.</summary>
    public static readonly MessageCode NegativeInventoryBlocked = new("INV.STOCK.NEGATIVE_BLOCKED");

    /// <summary>Stock went below zero, which is permitted here, and the cost is an estimate.</summary>
    public static readonly MessageCode NegativeInventoryAllowed = new("INV.STOCK.WENT_NEGATIVE");

    /// <summary>An estimated cost has been settled against what the goods really cost.</summary>
    public static readonly MessageCode CostSettled = new("INV.COST.SETTLED");

    /// <summary>Stock has fallen to or below its reorder point.</summary>
    public static readonly MessageCode BelowReorderPoint = new("INV.ITEM.BELOW_REORDER_POINT");

    /// <summary>The item is withdrawn from use.</summary>
    public static readonly MessageCode ItemBlocked = new("INV.ITEM.BLOCKED");

    /// <summary>The location is withdrawn from use.</summary>
    public static readonly MessageCode LocationBlocked = new("INV.LOCATION.BLOCKED");

    /// <summary>No item carries that number.</summary>
    public static readonly MessageCode ItemNotFound = new("INV.ITEM.NOT_FOUND");

    /// <summary>No location carries that code.</summary>
    public static readonly MessageCode LocationNotFound = new("INV.LOCATION.NOT_FOUND");

    /// <summary>A transfer was submitted with nothing on it.</summary>
    public static readonly MessageCode TransferNoLines = new("INV.TRANSFER.NO_LINES");

    /// <summary>Something tried to sell from a location that does not release stock.</summary>
    public static readonly MessageCode LocationNotSellable = new("INV.LOCATION.NOT_SELLABLE");

    /// <summary>A movement carried no quantity.</summary>
    public static readonly MessageCode QuantityZero = new("INV.MOVEMENT.QUANTITY_ZERO");

    /// <summary>Something tried to change the costing method after entries had posted.</summary>
    public static readonly MessageCode CostingMethodLocked = new("INV.ITEM.COSTING_METHOD_LOCKED");

    /// <summary>A transfer names one location as both source and destination.</summary>
    public static readonly MessageCode TransferToSameLocation = new("INV.TRANSFER.SAME_LOCATION");

    /// <summary>The stock count named does not exist.</summary>
    public static readonly MessageCode CountNotFound = new("INV.COUNT.NOT_FOUND");

    /// <summary>Something tried to change a count that has been posted.</summary>
    public static readonly MessageCode CountAlreadyPosted = new("INV.COUNT.ALREADY_POSTED");

    /// <summary>A count posted with lines nobody had reached.</summary>
    public static readonly MessageCode CountIncomplete = new("INV.COUNT.INCOMPLETE");

    /// <summary>A count where everything agreed with the system.</summary>
    public static readonly MessageCode CountNoDifferences = new("INV.COUNT.NO_DIFFERENCES");

    /// <summary>A count sheet that is already open for the same location.</summary>
    public static readonly MessageCode CountAlreadyOpen = new("INV.COUNT.ALREADY_OPEN");

    /// <summary>The transfer named does not exist.</summary>
    public static readonly MessageCode TransferNotFound = new("INV.TRANSFER.NOT_FOUND");

    /// <summary>Something tried to ship a transfer that has already gone.</summary>
    public static readonly MessageCode TransferAlreadyShipped = new("INV.TRANSFER.ALREADY_SHIPPED");

    /// <summary>Something tried to receive a transfer that has not shipped.</summary>
    public static readonly MessageCode TransferNotShipped = new("INV.TRANSFER.NOT_SHIPPED");

    /// <summary>There is nothing left on the transfer to move.</summary>
    public static readonly MessageCode TransferNothingToMove = new("INV.TRANSFER.NOTHING_TO_MOVE");

    /// <summary>Less arrived than was sent.</summary>
    public static readonly MessageCode TransferShortReceipt = new("INV.TRANSFER.SHORT_RECEIPT");

    /// <summary>The company has nowhere to hold goods while they travel.</summary>
    public static readonly MessageCode NoInTransitLocation = new("INV.TRANSFER.NO_TRANSIT_LOCATION");

    /// <summary>Every message the module declares.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = BarcodeNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing carries that barcode", "لا يوجد ما يحمل هذا الباركود"),
            Detail = new LocalizedText(
                "Nothing in this company is set up under {Barcode}, on an item or on any of its "
                + "units.",
                "لا يوجد في هذه الشركة ما هو مسجّل تحت الباركود {Barcode}، لا على صنف ولا على أي "
                + "من وحداته."),
            Resolution = new LocalizedText(
                "Key the item number instead, and add the barcode to the item afterwards so the "
                + "next person does not have to. A case and a single carry different barcodes, "
                + "so check which one was scanned.",
                "أدخل رقم الصنف بدلًا من ذلك، ثم أضف الباركود إلى الصنف لاحقًا حتى لا يضطر من "
                + "بعدك لذلك. والكرتون والحبة الواحدة يحملان باركودين مختلفين، فتحقق من أيهما "
                + "مُسح."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = UnitNotSetUpForItem,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "That unit is not set up for this item", "هذه الوحدة غير معرّفة لهذا الصنف"),
            Detail = new LocalizedText(
                "{ItemNo} is counted in {BaseUnit}, and nothing says how many {BaseUnit} are in "
                + "one {UnitCode}.",
                "يُحسب الصنف {ItemNo} بوحدة {BaseUnit}، ولا شيء يحدد كم {BaseUnit} في {UnitCode} "
                + "واحد."),
            Resolution = new LocalizedText(
                "Add the unit to this item and say how many it holds. A box is a fact about the "
                + "item rather than about boxes: one item's box is twelve and another's is six, "
                + "so each has to say.",
                "أضف الوحدة إلى هذا الصنف وحدّد ما تحتويه. فالكرتون حقيقة تخص الصنف لا الكراتين: "
                + "فكرتون صنف اثنا عشر وكرتون آخر ستة، فعلى كل صنف أن يحدد."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = UnitFactorNotUsable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That unit holds nothing", "هذه الوحدة لا تحتوي شيئًا"),
            Detail = new LocalizedText(
                "{UnitCode} on {ItemNo} says it holds nought base units, so every quantity keyed "
                + "in it would come to nothing.",
                "تقول الوحدة {UnitCode} على الصنف {ItemNo} إنها تحتوي صفرًا من الوحدات الأساسية، "
                + "فكل كمية تُدخل بها ستؤول إلى لا شيء."),
            Resolution = new LocalizedText(
                "Set how many it holds. Refused rather than posted, because nought would read as "
                + "a clean zero on every report instead of as a mistake.",
                "حدّد ما تحتويه. وقد رُفض بدل الترحيل، لأن الصفر سيبدو في كل تقرير رقمًا نظيفًا "
                + "لا خطأً."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = ItemNotFoundForUnit,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such item", "لا يوجد صنف بهذا الرقم"),
            Detail = new LocalizedText(
                "Nothing in this company is numbered {ItemNo}.",
                "لا يوجد في هذه الشركة صنف برقم {ItemNo}."),
            Resolution = new LocalizedText(
                "Check the number, or scan the barcode instead.",
                "تحقق من الرقم، أو امسح الباركود بدلًا من ذلك."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = QuantityTooPrecise,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That unit is not sold in fractions", "هذه الوحدة لا تُباع بالكسور"),
            Detail = new LocalizedText(
                "{Quantity:0.#####} was entered against {UnitCode}, which is counted to "
                + "{DecimalPlaces} decimal places.",
                "أُدخلت الكمية {Quantity:0.#####} بالوحدة {UnitCode}، وهي تُحسب بـ {DecimalPlaces} "
                + "من المنازل العشرية."),
            Resolution = new LocalizedText(
                "Enter a quantity the unit can carry, or use a unit that can. A till accepting "
                + "half of something sold one at a time has taken an order nobody can pick.",
                "أدخل كمية تستوعبها الوحدة، أو استخدم وحدة تستوعبها. فنقطة البيع التي تقبل نصف "
                + "صنف يُباع بالحبة قد سجّلت طلبًا لا يستطيع أحد تجهيزه."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = UnitCodeRequired,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A unit needs a code", "الوحدة تحتاج رمزًا"),
            Detail = new LocalizedText(
                "No code was given, and a unit is named by its code on every document.",
                "لم يُعطَ رمز، والوحدة تُسمّى برمزها في كل مستند."),
            Resolution = new LocalizedText(
                "Give it a short code people will recognise on a receipt: PCS, KG, BOX.",
                "أعطها رمزًا قصيرًا يعرفه الناس على الفاتورة: PCS أو KG أو BOX."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = DecimalPlacesOutOfRange,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("More precision than a quantity holds", "دقة تفوق ما تحتمله الكمية"),
            Detail = new LocalizedText(
                "{UnitCode} asks for {DecimalPlaces} decimal places, and a quantity holds "
                + "{Maximum}.",
                "تطلب الوحدة {UnitCode} عدد {DecimalPlaces} من المنازل العشرية، والكمية تحتمل "
                + "{Maximum}."),
            Resolution = new LocalizedText(
                "Ask for {Maximum} or fewer. Promising a precision the database rounds away would "
                + "mean a quantity that changes when it is saved.",
                "اطلب {Maximum} أو أقل. فوعدٌ بدقة يقرّبها المخزن يعني كمية تتغير عند حفظها."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = UnitNotInCompanyList,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such unit", "لا توجد وحدة بهذا الرمز"),
            Detail = new LocalizedText(
                "{UnitCode} is not on this company's list of units.",
                "الرمز {UnitCode} ليس في قائمة وحدات هذه الشركة."),
            Resolution = new LocalizedText(
                "Add it to the unit list first. Free text here is how one company ends up with "
                + "CTN, CARTON and CASE all meaning the same thing and none of them adding up.",
                "أضفها إلى قائمة الوحدات أولًا. فالنص الحر هنا هو ما يجعل شركة تحمل CTN وCARTON "
                + "وCASE بمعنى واحد ولا يجتمع منها شيء."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = BaseUnitFactorMustBeOne,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("One of the base unit is one", "الوحدة الأساسية واحدها واحد"),
            Detail = new LocalizedText(
                "{UnitCode} is what {ItemNo} is counted in, and it was given a factor of "
                + "{QuantityPerUnit:0.#####}.",
                "الوحدة {UnitCode} هي ما يُحسب به الصنف {ItemNo}، وقد أُعطيت معاملًا قدره "
                + "{QuantityPerUnit:0.#####}."),
            Resolution = new LocalizedText(
                "Set it to one, or leave it out altogether -- the base unit needs no row. Anything "
                + "else says the item is counted in something other than what it is counted in, "
                + "and every stock figure it has would be wrong by that factor.",
                "اجعله واحدًا، أو احذف السطر أصلًا — فالوحدة الأساسية لا تحتاج سطرًا. وأي قيمة "
                + "أخرى تعني أن الصنف يُحسب بغير ما يُحسب به، فيصير كل رصيد له خاطئًا بذلك المعامل."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = BarcodeAlreadyInUse,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Something else carries that barcode", "الباركود مستخدم بالفعل"),
            Detail = new LocalizedText(
                "{Barcode} is already on {ItemNo} {UnitCode}.",
                "الباركود {Barcode} مسجّل بالفعل على {ItemNo} {UnitCode}."),
            Resolution = new LocalizedText(
                "Use a different barcode, or take it off the other one first. Two rows carrying "
                + "the same barcode makes a scan return whichever the database reached first, "
                + "which nobody notices until a stock count.",
                "استخدم باركودًا آخر، أو أزله من الآخر أولًا. فوجود سطرين بالباركود نفسه يجعل "
                + "المسح يعيد أيهما وصل إليه المخزن أولًا، ولا يلاحظ ذلك أحد حتى الجرد."),
            HelpTopic = "inventory/units-of-measure",
        },
        new()
        {
            Code = NegativeInventoryBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Not enough stock", "الكمية غير كافية"),
            Detail = new LocalizedText(
                "{Requested:0.#####} of {ItemNo} {ItemName} was requested at {Location}, and {AvailableQuantity:0.#####} "
                + "is on hand. This company does not allow stock to go below zero.",
                "تم طلب {Requested:0.#####} من {ItemNo} {ItemName} في {Location}، والمتوفر {AvailableQuantity:0.#####}. "
                + "لا تسمح هذه الشركة بأن يقل المخزون عن الصفر."),
            Resolution = new LocalizedText(
                "Reduce the quantity to {AvailableQuantity:0.#####}, receive the goods first, or ask an "
                + "administrator to allow negative stock for this item.",
                "قلّل الكمية إلى {AvailableQuantity:0.#####}، أو استلم البضاعة أولاً، أو اطلب من المسؤول السماح "
                + "بالمخزون السالب لهذا الصنف."),
            OverridePermission = "Inventory.Stock.Override",
            HelpTopic = "inventory/negative-stock",
        },
        new()
        {
            Code = NegativeInventoryAllowed,

            // A warning, not a block. The sale goes through, and this is the record that its cost
            // is provisional -- which is the difference between permitting negative stock and
            // losing track of what it did to the books.
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Stock has gone below zero", "أصبح المخزون بالسالب"),
            Detail = new LocalizedText(
                "{ItemNo} {ItemName} is now {BalanceQuantity:0.#####} at {Location}. {ShortfallQuantity:0.#####} unit(s) were "
                + "valued at an estimated {EstimatedUnitCost:N2} each.",
                "أصبح {ItemNo} {ItemName} الآن {BalanceQuantity:0.#####} في {Location}. تم تقييم {ShortfallQuantity:0.#####} وحدة "
                + "بتكلفة تقديرية {EstimatedUnitCost:N2} للوحدة."),
            Resolution = new LocalizedText(
                "Receive the goods as soon as the paperwork allows. ASAP will settle the estimate "
                + "against what they actually cost, and the difference posts then.",
                "استلم البضاعة فور اكتمال المستندات. سيقوم ASAP بتسوية التقدير مقابل التكلفة "
                + "الفعلية، ويُرحّل الفرق عندها."),
            HelpTopic = "inventory/negative-stock",
        },
        new()
        {
            Code = CostSettled,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("Estimated cost settled", "تمت تسوية التكلفة التقديرية"),
            Detail = new LocalizedText(
                "{Quantity:0.#####} unit(s) of {ItemNo} estimated at {EstimatedUnitCost:N2} actually cost "
                + "{ActualUnitCost:N2}. A correction of {Difference:N2} was posted.",
                "{Quantity:0.#####} وحدة من {ItemNo} قُدّرت بـ {EstimatedUnitCost:N2} وتكلفتها الفعلية "
                + "{ActualUnitCost:N2}. تم ترحيل تسوية بقيمة {Difference:N2}."),
            HelpTopic = "inventory/cost-adjustment",
        },
        new()
        {
            Code = BelowReorderPoint,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Item is below its reorder point", "الصنف تحت حد إعادة الطلب"),
            Detail = new LocalizedText(
                "{ItemNo} {ItemName} is down to {BalanceQuantity:0.#####} at {Location}, against a reorder point "
                + "of {ReorderPoint:0.#####}.",
                "انخفض {ItemNo} {ItemName} إلى {BalanceQuantity:0.#####} في {Location}، مقابل حد إعادة طلب "
                + "قدره {ReorderPoint:0.#####}."),
        },
        new()
        {
            Code = ItemBlocked,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That item is blocked", "هذا الصنف محظور"),
            Detail = new LocalizedText(
                "{ItemNo} {ItemName} has been withdrawn from use.",
                "تم سحب {ItemNo} {ItemName} من الاستخدام."),
            Resolution = new LocalizedText(
                "Choose a different item, or unblock {ItemNo} on the item card.",
                "اختر صنفًا آخر، أو ألغِ حظر {ItemNo} من بطاقة الصنف."),
        },
        new()
        {
            Code = LocationBlocked,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That location is blocked", "هذا الموقع محظور"),
            Detail = new LocalizedText(
                "Location {Location} has been withdrawn from use.",
                "تم سحب الموقع {Location} من الاستخدام."),
            Resolution = new LocalizedText(
                "Choose a different location, or unblock {Location} in location setup.",
                "اختر موقعًا آخر، أو ألغِ حظر {Location} في إعداد المواقع."),
        },
        new()
        {
            Code = ItemNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such item", "لا يوجد صنف بهذا الرقم"),
            Detail = new LocalizedText(
                "Nothing in this company is numbered {ItemNo}.",
                "لا يوجد في هذه الشركة أي صنف يحمل الرقم {ItemNo}."),
            Resolution = new LocalizedText(
                "Check the number against the item list, or create {ItemNo} first.",
                "تحقق من الرقم في قائمة الأصناف، أو أنشئ {ItemNo} أولاً."),
        },
        new()
        {
            Code = LocationNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such location", "لا يوجد موقع بهذا الرمز"),
            Detail = new LocalizedText(
                "No location in this company is coded {Location}.",
                "لا يوجد في هذه الشركة موقع يحمل الرمز {Location}."),
            Resolution = new LocalizedText(
                "Check the code against the location list, or create {Location} first.",
                "تحقق من الرمز في قائمة المواقع، أو أنشئ {Location} أولاً."),
        },
        new()
        {
            Code = TransferNoLines,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The transfer has no lines", "التحويل بدون سطور"),
            Detail = new LocalizedText(
                "A transfer from {From} to {To} was submitted with nothing on it.",
                "تم إرسال تحويل من {From} إلى {To} بدون أي أصناف."),
            Resolution = new LocalizedText(
                "Add at least one item and quantity before saving the transfer.",
                "أضف صنفًا واحدًا على الأقل مع الكمية قبل حفظ التحويل."),
        },
        new()
        {
            Code = LocationNotSellable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "Stock at that location cannot be sold",
                "لا يمكن بيع المخزون في هذا الموقع"),
            Detail = new LocalizedText(
                "{Location} is not marked as sellable. Its stock is counted in the valuation but "
                + "must not be promised to a customer -- which is how a warehouse, a quarantine "
                + "bay and goods in transit are each kept out of the sales channel.",
                "الموقع {Location} غير محدد كموقع بيع. يُحتسب مخزونه ضمن التقييم ولكن لا يجوز "
                + "الالتزام به لعميل، وبهذا يبقى المستودع ومنطقة الحجر والبضائع في الطريق خارج "
                + "دورة البيع."),
            Resolution = new LocalizedText(
                "Sell from a location that is marked sellable, transfer the goods to one, or mark "
                + "{Location} sellable if it should have been.",
                "بِع من موقع محدد كموقع بيع، أو انقل البضاعة إليه، أو حدِّد {Location} كموقع بيع "
                + "إن كان يجب أن يكون كذلك."),
            OverridePermission = "Inventory.Stock.Override",
        },
        new()
        {
            Code = QuantityZero,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A movement has no quantity", "حركة بدون كمية"),
            Detail = new LocalizedText(
                "Line {LineNo} would move nothing.",
                "السطر {LineNo} لن يحرّك أي كمية."),
            Resolution = new LocalizedText(
                "Enter a quantity on line {LineNo}, or remove the line.",
                "أدخل كمية في السطر {LineNo}، أو احذف السطر."),
        },
        new()
        {
            Code = CostingMethodLocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "The costing method can no longer be changed",
                "لم يعد بالإمكان تغيير طريقة التكلفة"),
            Detail = new LocalizedText(
                "{ItemNo} has posted entries valued using {CurrentMethod}. Changing the method now "
                + "would leave old entries valued one way and new ones another, and no report could "
                + "reconcile the two.",
                "الصنف {ItemNo} لديه قيود مرحّلة مقيّمة بطريقة {CurrentMethod}. تغيير الطريقة الآن "
                + "سيجعل القيود القديمة مقيّمة بطريقة والجديدة بأخرى، ولا يمكن لأي تقرير التوفيق بينهما."),
            Resolution = new LocalizedText(
                "Create a new item with the method you want and withdraw this one, or ask your "
                + "ASAP partner about a revaluation.",
                "أنشئ صنفًا جديدًا بالطريقة المطلوبة واسحب هذا الصنف، أو استشر شريك ASAP بشأن إعادة التقييم."),
            HelpTopic = "inventory/costing-methods",
        },
        new()
        {
            Code = CountNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such count", "لا يوجد جرد بهذا الرقم"),
            Detail = new LocalizedText(
                "No stock count in this company is numbered {CountNo}.",
                "لا يوجد في هذه الشركة جرد يحمل الرقم {CountNo}."),
            Resolution = new LocalizedText(
                "Check the number against the count list.",
                "تحقق من الرقم في قائمة عمليات الجرد."),
            HelpTopic = "inventory/stock-count",
        },
        new()
        {
            Code = CountAlreadyPosted,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This count has been posted", "تم ترحيل هذا الجرد"),
            Detail = new LocalizedText(
                "{CountNo} posted as transaction {TransactionNo}. Its differences are movements "
                + "now, and posting it again would move the same stock twice.",
                "رُحّل الجرد {CountNo} ضمن الحركة {TransactionNo}. وفروقه أصبحت حركات، وترحيله "
                + "ثانية يحرّك المخزون نفسه مرتين."),
            Resolution = new LocalizedText(
                "Count again. A second count is the honest way to correct a first one, and both "
                + "stay on the record where somebody can see what changed between them.",
                "أجرِ جردًا جديدًا. فالجرد الثاني هو الطريق الصادق لتصحيح الأول، ويبقى الاثنان في "
                + "السجل حيث يرى المطّلع ما تغيّر بينهما."),
            HelpTopic = "inventory/stock-count",
        },
        new()
        {
            Code = CountIncomplete,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Some shelves were not reached", "لم تُجرد بعض الأصناف"),
            Detail = new LocalizedText(
                "{CountNo} has {NotCountedQuantity} lines nobody has counted. A line nobody looked "
                + "at is not a line found empty, and posting the sheet would write off everything "
                + "the counters ran out of time for.",
                "في الجرد {CountNo} عدد {NotCountedQuantity} من الأسطر لم يجردها أحد. والسطر الذي "
                + "لم ينظر إليه أحد ليس سطرًا وُجد فارغًا، وترحيل الورقة يشطب كل ما لم يسع الوقت "
                + "لجرده."),
            Resolution = new LocalizedText(
                "Count them, or enter nought where the shelf really is empty. If the rest is "
                + "right and you mean to post anyway, somebody with the override permission may "
                + "do so and say why — the uncounted lines are left exactly as they were.",
                "اجردها، أو أدخل صفرًا حيث يكون الرف فارغًا فعلاً. وإن كان الباقي صحيحًا وأردت "
                + "الترحيل رغم ذلك، فبإمكان من يملك صلاحية التجاوز فعل ذلك مع بيان السبب — وتبقى "
                + "الأسطر غير المجرودة كما هي تمامًا."),
            OverridePermission = "Inventory.Stock.Override",
            HelpTopic = "inventory/stock-count",
        },
        new()
        {
            Code = CountNoDifferences,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Everything agreed", "لا فروق"),
            Detail = new LocalizedText(
                "{CountNo} found exactly what the system said, so nothing was posted.",
                "وجد الجرد {CountNo} ما يقوله النظام تمامًا، فلم يُرحَّل شيء."),
            Resolution = new LocalizedText(
                "The count is closed regardless, which is the record that it was done. A shop "
                + "that counted and matched is worth knowing about as much as one that did not.",
                "أُقفل الجرد على أي حال، وهو سجل بأنه أُجري. فالمتجر الذي جرد وطابق يستحق التسجيل "
                + "كما يستحقه الذي لم يطابق."),
            HelpTopic = "inventory/stock-count",
        },
        new()
        {
            Code = CountAlreadyOpen,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A count is already open there", "يوجد جرد مفتوح لهذا الموقع"),
            Detail = new LocalizedText(
                "{CountNo} is open for {LocationCode}. Two sheets for one location would each be "
                + "measured against a different moment, and posting both would apply the same "
                + "difference twice.",
                "الجرد {CountNo} مفتوح للموقع {LocationCode}. ووجود ورقتين لموقع واحد يجعل كلاً "
                + "منهما مقيسة على لحظة مختلفة، وترحيلهما معًا يطبّق الفرق نفسه مرتين."),
            Resolution = new LocalizedText(
                "Finish or cancel {CountNo} first.",
                "أنهِ الجرد {CountNo} أو ألغه أولاً."),
            HelpTopic = "inventory/stock-count",
        },
        new()
        {
            Code = TransferNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That transfer does not exist", "هذا التحويل غير موجود"),
            Detail = new LocalizedText(
                "No transfer numbered {TransferNo} was found in this company.",
                "لا يوجد تحويل برقم {TransferNo} في هذه الشركة."),
            Resolution = new LocalizedText(
                "Check the number, or look for it in another company.",
                "تحقق من الرقم، أو ابحث عنه في شركة أخرى."),
        },
        new()
        {
            Code = TransferAlreadyShipped,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That transfer has already shipped", "تم شحن هذا التحويل بالفعل"),
            Detail = new LocalizedText(
                "Transfer {TransferNo} from {From} is {Status}. Shipping again would send the goods twice.",
                "التحويل {TransferNo} من {From} حالته {Status}. إعادة الشحن سترسل البضاعة مرتين."),
            Resolution = new LocalizedText(
                "Receive it at {To}, or raise a new transfer for anything still to send.",
                "استلمه في {To}، أو أنشئ تحويلاً جديدًا لما تبقى إرساله."),
        },
        new()
        {
            Code = TransferNotShipped,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That transfer has not shipped yet", "لم يتم شحن هذا التحويل بعد"),
            Detail = new LocalizedText(
                "Transfer {TransferNo} is {Status}, so nothing has left {From} to arrive at {To}.",
                "التحويل {TransferNo} حالته {Status}، فلم تغادر أي بضاعة {From} لتصل إلى {To}."),
            Resolution = new LocalizedText(
                "Ship it from {From} first.",
                "قم بشحنه من {From} أولاً."),
        },
        new()
        {
            Code = TransferNothingToMove,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nothing left to move", "لا يوجد ما يمكن نقله"),
            Detail = new LocalizedText(
                "Every line on transfer {TransferNo} has already been dealt with.",
                "تمت معالجة جميع سطور التحويل {TransferNo}."),
            Resolution = new LocalizedText(
                "Add lines, or raise a new transfer.",
                "أضف سطورًا، أو أنشئ تحويلاً جديدًا."),
        },
        new()
        {
            Code = TransferShortReceipt,

            // A warning, not a refusal. The goods that did arrive are received; what did not is
            // left in transit for somebody to investigate rather than written off by default.
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Less arrived than was sent", "الكمية الواصلة أقل من المرسلة"),
            Detail = new LocalizedText(
                "{ShortfallQuantity:0.#####} unit(s) from transfer {TransferNo} are still at {Location}.",
                "لا تزال {ShortfallQuantity:0.#####} وحدة من التحويل {TransferNo} في {Location}."),
            Resolution = new LocalizedText(
                "Receive them when they turn up. If they are lost, write them off with a negative "
                + "adjustment so the loss is recorded rather than assumed.",
                "استلمها عند ظهورها. وإذا فُقدت، اشطبها بتسوية سالبة ليُسجَّل الفقد بدلاً من افتراضه."),
            HelpTopic = "inventory/transfers",
        },
        new()
        {
            Code = NoInTransitLocation,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "There is nowhere to hold goods in transit",
                "لا يوجد موقع لحفظ البضاعة أثناء النقل"),
            Detail = new LocalizedText(
                "Transfer {TransferNo} moves goods from {From} to {To}, and this company has no "
                + "in-transit location for them to travel through.",
                "ينقل التحويل {TransferNo} بضاعة من {From} إلى {To}، ولا يوجد في هذه الشركة موقع "
                + "للنقل تمر عبره."),
            Resolution = new LocalizedText(
                "Create a location marked as in transit. Without one the goods would vanish from "
                + "the valuation for the length of the journey.",
                "أنشئ موقعًا محددًا كموقع نقل. بدونه ستختفي البضاعة من التقييم طوال مدة الرحلة."),
            HelpTopic = "inventory/transfers",
        },
        new()
        {
            Code = TransferToSameLocation,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A transfer must move somewhere", "يجب أن ينقل التحويل إلى مكان آخر"),
            Detail = new LocalizedText(
                "{Location} is named as both the source and the destination.",
                "تم تحديد {Location} كمصدر ووجهة في آنٍ واحد."),
            Resolution = new LocalizedText(
                "Choose a different destination.",
                "اختر وجهة مختلفة."),
        },
    ];
}
