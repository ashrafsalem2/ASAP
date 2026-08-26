using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Inventory;

/// <summary>
/// Everything the Inventory module can tell the user.
/// </summary>
public static class InventoryMessages
{
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

    /// <summary>Something tried to sell from a location that does not release stock.</summary>
    public static readonly MessageCode LocationNotSellable = new("INV.LOCATION.NOT_SELLABLE");

    /// <summary>A movement carried no quantity.</summary>
    public static readonly MessageCode QuantityZero = new("INV.MOVEMENT.QUANTITY_ZERO");

    /// <summary>Something tried to change the costing method after entries had posted.</summary>
    public static readonly MessageCode CostingMethodLocked = new("INV.ITEM.COSTING_METHOD_LOCKED");

    /// <summary>A transfer names one location as both source and destination.</summary>
    public static readonly MessageCode TransferToSameLocation = new("INV.TRANSFER.SAME_LOCATION");

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
            Code = NegativeInventoryBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Not enough stock", "الكمية غير كافية"),
            Detail = new LocalizedText(
                "{Requested:0.#####} of {ItemNo} {ItemName} was requested at {Location}, and {Available:0.#####} "
                + "is on hand. This company does not allow stock to go below zero.",
                "تم طلب {Requested:0.#####} من {ItemNo} {ItemName} في {Location}، والمتوفر {Available:0.#####}. "
                + "لا تسمح هذه الشركة بأن يقل المخزون عن الصفر."),
            Resolution = new LocalizedText(
                "Reduce the quantity to {Available:0.#####}, receive the goods first, or ask an "
                + "administrator to allow negative stock for this item.",
                "قلّل الكمية إلى {Available:0.#####}، أو استلم البضاعة أولاً، أو اطلب من المسؤول السماح "
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
                "{ItemNo} {ItemName} is now {Balance:0.#####} at {Location}. {Shortfall:0.#####} unit(s) were "
                + "valued at an estimated {EstimatedUnitCost:N2} each.",
                "أصبح {ItemNo} {ItemName} الآن {Balance:0.#####} في {Location}. تم تقييم {Shortfall:0.#####} وحدة "
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
                "{ItemNo} {ItemName} is down to {Balance:0.#####} at {Location}, against a reorder point "
                + "of {ReorderPoint:0.#####}.",
                "انخفض {ItemNo} {ItemName} إلى {Balance:0.#####} في {Location}، مقابل حد إعادة طلب "
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
            Code = LocationNotSellable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "Stock at that location cannot be sold",
                "لا يمكن بيع المخزون في هذا الموقع"),
            Detail = new LocalizedText(
                "{Location} holds stock that has not been released -- goods awaiting checking, or "
                + "in transit. It is counted in the valuation but must not be promised to a customer.",
                "يحتوي {Location} على مخزون لم يُفرج عنه، مثل بضائع قيد الفحص أو في الطريق. "
                + "يُحتسب ضمن التقييم ولكن لا يجوز الالتزام به لعميل."),
            Resolution = new LocalizedText(
                "Move the goods to a sellable location first, or sell from one.",
                "انقل البضاعة إلى موقع قابل للبيع أولاً، أو بِع من موقع كذلك."),
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
                "{Shortfall:0.#####} unit(s) from transfer {TransferNo} are still at {Location}.",
                "لا تزال {Shortfall:0.#####} وحدة من التحويل {TransferNo} في {Location}."),
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
