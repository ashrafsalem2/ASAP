using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Promotions;

/// <summary>
/// Everything the promotions module can refuse, and why.
/// </summary>
/// <remarks>
/// The one that matters is <see cref="BelowMarginFloor"/>. An offer that sells below cost is not
/// usually somebody being reckless — it is somebody who priced against last quarter's cost, or
/// who discounted a category without noticing what was in it. The message therefore names the
/// item, what it costs today, what the offer would charge and the size of the hole, because those
/// four figures together are what turns "no" into something a person can act on.
/// </remarks>
public static class PromotionsMessages
{
    /// <summary>An offer would sell something below the floor the company set.</summary>
    public static readonly MessageCode BelowMarginFloor = new("PRM.OFFER.BELOW_MARGIN_FLOOR");

    /// <summary>An offer was left out of a basket because it would have broken the floor.</summary>
    public static readonly MessageCode OfferNotApplied = new("PRM.OFFER.NOT_APPLIED");

    /// <summary>The offer named does not exist.</summary>
    public static readonly MessageCode OfferNotFound = new("PRM.OFFER.NOT_FOUND");

    /// <summary>An offer was saved with a code another one already has.</summary>
    public static readonly MessageCode OfferCodeTaken = new("PRM.OFFER.CODE_TAKEN");

    /// <summary>An offer's window ends before it starts.</summary>
    public static readonly MessageCode WindowEndsBeforeItStarts = new("PRM.OFFER.BAD_WINDOW");

    /// <summary>An offer was saved with nothing to apply to.</summary>
    public static readonly MessageCode OfferHasNoTargets = new("PRM.OFFER.NO_TARGETS");

    /// <summary>A percentage outside nought to a hundred.</summary>
    public static readonly MessageCode PercentageOutOfRange = new("PRM.OFFER.BAD_PERCENTAGE");

    /// <summary>A buy-X-get-Y with nothing to buy or nothing to get.</summary>
    public static readonly MessageCode BuyGetIncomplete = new("PRM.OFFER.BAD_BUY_GET");

    /// <summary>A coupon that does not match any offer running today.</summary>
    public static readonly MessageCode CouponNotRecognised = new("PRM.COUPON.NOT_RECOGNISED");

    /// <summary>An offer applied, said so that somebody reading a receipt knows why.</summary>
    public static readonly MessageCode OfferApplied = new("PRM.OFFER.APPLIED");

    /// <summary>No account is set up for what promotions give away.</summary>
    public static readonly MessageCode NoPromotionAccount = new("PRM.SETUP.NO_ACCOUNT");

    /// <summary>Every message the module can raise.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = BelowMarginFloor,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "This offer sells below the floor",
                "هذا العرض يبيع بأقل من الحد الأدنى"),
            Detail = new LocalizedText(
                "{OfferCode} would sell {ItemNo} {Description} at {OfferPrice:N2} against a cost "
                + "of {UnitCost:N2}, a margin of {MarginPercent:N2}% where {FloorPercent:N2}% is "
                + "the least this company accepts. That is {Shortfall:N2} a unit.",
                "العرض {OfferCode} سيبيع {ItemNo} {Description} بسعر {OfferPrice:N2} مقابل تكلفة "
                + "{UnitCost:N2}، بهامش {MarginPercent:N2}% بينما الحد الأدنى المقبول في الشركة "
                + "{FloorPercent:N2}%. أي بفارق {Shortfall:N2} لكل وحدة."),
            Resolution = new LocalizedText(
                "Reduce the offer, take {ItemNo} out of it, or have somebody holding "
                + "Promotions.Offer.Override approve it. Costs move, so an offer that was "
                + "sound when it was written may not be today.",
                "خفّض العرض، أو استبعد {ItemNo} منه، أو اطلب اعتماده ممن يملك صلاحية "
                + "Promotions.Offer.Override. فالتكاليف تتغير، وقد لا يكون العرض السليم عند "
                + "إعداده سليمًا اليوم."),
            OverridePermission = "Promotions.Offer.Override",
            HelpTopic = "promotions/margin",
        },
        new()
        {
            Code = OfferNotApplied,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("An offer did not apply", "لم يُطبَّق أحد العروض"),
            Detail = new LocalizedText(
                "{OfferCode} would have sold {ItemNo} {Description} at {OfferPrice:N2} against a "
                + "cost of {UnitCost:N2}, a margin of {MarginPercent:N2}% where {FloorPercent:N2}% "
                + "is the least this company accepts. The sale went through at the ordinary price.",
                "كان العرض {OfferCode} سيبيع {ItemNo} {Description} بسعر {OfferPrice:N2} مقابل "
                + "تكلفة {UnitCost:N2}، بهامش {MarginPercent:N2}% بينما الحد الأدنى المقبول "
                + "{FloorPercent:N2}%. وقد تمت العملية بالسعر العادي."),
            Resolution = new LocalizedText(
                "Nothing to do at the counter. Somebody who maintains offers should look at "
                + "{OfferCode}: costs move, and one that was sound when it was written may not be "
                + "today.",
                "لا إجراء عند نقطة البيع. وعلى من يدير العروض مراجعة {OfferCode}، فالتكاليف تتغير "
                + "وقد لا يكون العرض السليم عند إعداده سليمًا اليوم."),
            HelpTopic = "promotions/margin",
        },
        new()
        {
            Code = OfferNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such offer", "لا يوجد عرض بهذا الرمز"),
            Detail = new LocalizedText(
                "No offer in this company is coded {OfferCode}.",
                "لا يوجد في هذه الشركة عرض يحمل الرمز {OfferCode}."),
            Resolution = new LocalizedText(
                "Check the code against the offer list.",
                "تحقق من الرمز في قائمة العروض."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = OfferCodeTaken,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That code is in use", "الرمز مستخدم بالفعل"),
            Detail = new LocalizedText(
                "{OfferCode} already belongs to {ExistingName}.",
                "الرمز {OfferCode} مخصص بالفعل للعرض {ExistingName}."),
            Resolution = new LocalizedText(
                "Choose another code. An offer code appears on receipts, so two offers sharing "
                + "one would make a month's takings impossible to attribute.",
                "اختر رمزًا آخر. فرمز العرض يظهر على الإيصالات، ووجود عرضين برمز واحد يجعل نسبة "
                + "مبيعات الشهر إلى مصدرها متعذرة."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = WindowEndsBeforeItStarts,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The offer ends before it starts", "العرض ينتهي قبل أن يبدأ"),
            Detail = new LocalizedText(
                "{OfferCode} starts on {StartsOn:d} and ends on {EndsOn:d}, so it never runs.",
                "العرض {OfferCode} يبدأ في {StartsOn:d} وينتهي في {EndsOn:d}، فلا يعمل مطلقًا."),
            Resolution = new LocalizedText(
                "Set the end date after the start date, or leave it empty for an open-ended offer.",
                "اجعل تاريخ الانتهاء بعد تاريخ البدء، أو اتركه فارغًا لعرض مفتوح المدة."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = OfferHasNoTargets,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The offer applies to nothing", "العرض لا ينطبق على شيء"),
            Detail = new LocalizedText(
                "{OfferCode} is scoped to {Scope} and names none.",
                "العرض {OfferCode} نطاقه {Scope} ولم يُحدَّد له أي عنصر."),
            Resolution = new LocalizedText(
                "Name at least one, or scope the offer to everything if a store-wide sale is what "
                + "was meant.",
                "حدّد عنصرًا واحدًا على الأقل، أو اجعل نطاق العرض شاملاً إن كان المقصود تخفيضًا عامًا."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = PercentageOutOfRange,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That percentage is not possible", "نسبة غير ممكنة"),
            Detail = new LocalizedText(
                "{OfferCode} takes {Percentage:N2}% off. A discount below nought adds to the price, and "
                + "one above a hundred pays the customer to shop.",
                "العرض {OfferCode} يخصم {Percentage:N2}%. والخصم دون الصفر يزيد السعر، وما يتجاوز المئة "
                + "يجعل الشركة تدفع للعميل ليشتري."),
            Resolution = new LocalizedText(
                "Enter a percentage between nought and a hundred.",
                "أدخل نسبة بين صفر ومئة."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = BuyGetIncomplete,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The deal is incomplete", "العرض غير مكتمل"),
            Detail = new LocalizedText(
                "{OfferCode} says buy {BuyQuantity:0.#####} and get {GetQuantity:0.#####}. Both "
                + "have to be more than nothing for the deal to mean anything.",
                "العرض {OfferCode} ينص على شراء {BuyQuantity:0.#####} والحصول على "
                + "{GetQuantity:0.#####}. ويجب أن يكون كلاهما أكبر من صفر ليكون للعرض معنى."),
            Resolution = new LocalizedText(
                "Set both quantities. Three for two is buy two, get one.",
                "حدّد الكميتين. فثلاثة بسعر اثنين تعني شراء اثنين والحصول على واحد."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = CouponNotRecognised,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("That coupon does nothing here", "هذه القسيمة لا تنطبق"),
            Detail = new LocalizedText(
                "No offer running today is unlocked by {CouponCode}. It may have expired, or "
                + "belong to another branch or channel.",
                "لا يوجد عرض ساري اليوم تفتحه القسيمة {CouponCode}. ربما انتهت صلاحيتها أو كانت "
                + "لفرع أو قناة أخرى."),
            Resolution = new LocalizedText(
                "Check the dates printed on it. The sale can go through without it.",
                "تحقق من التواريخ المطبوعة عليها. ويمكن إتمام البيع بدونها."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = OfferApplied,
            Severity = MessageSeverity.Success,
            Title = new LocalizedText("An offer applied", "تم تطبيق عرض"),
            Detail = new LocalizedText(
                "{OfferName} took {Amount:N2} off.",
                "العرض {OfferName} خصم {Amount:N2}."),
            HelpTopic = "promotions/offers",
        },
        new()
        {
            Code = NoPromotionAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText(
                "Nowhere to post what the offer gave away",
                "لا يوجد حساب لترحيل قيمة العرض"),
            Detail = new LocalizedText(
                "No promotions account is set up, so {Amount:N2} given away by {OfferCode} has "
                + "nowhere to go.",
                "لا يوجد حساب للعروض، فمبلغ {Amount:N2} الممنوح ضمن العرض {OfferCode} بلا وجهة."),
            Resolution = new LocalizedText(
                "Set Promotions.Posting.DiscountAccount in setup. It is deliberately separate from "
                + "the ordinary sales discount: both are money given away, and only one of them is "
                + "a campaign somebody should be able to total.",
                "حدّد الإعداد Promotions.Posting.DiscountAccount. وهو منفصل عمدًا عن خصم المبيعات "
                + "العادي، فكلاهما مبالغ ممنوحة، لكن أحدهما حملة تسويقية يجب أن يمكن حصر تكلفتها."),
            HelpTopic = "promotions/setup",
        },
    ];
}
