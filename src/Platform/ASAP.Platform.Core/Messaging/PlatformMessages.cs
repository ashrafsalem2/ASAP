using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Core.Messaging;

/// <summary>
/// The messages the platform itself can raise, as distinct from those belonging to a module.
/// </summary>
/// <remarks>
/// Registered at startup alongside every module catalogue, so a platform refusal reads exactly
/// like a module one: same shape, same translations, same rule that a block must offer a way
/// forward.
/// </remarks>
public static class PlatformMessages
{
    /// <summary>The caller lacks the permission an operation requires.</summary>
    public static readonly MessageCode PermissionDenied = new("SEC.PERMISSION.DENIED");

    /// <summary>The caller has not chosen a company to work in.</summary>
    public static readonly MessageCode NoCompanySelected = new("SEC.COMPANY.NOT_SELECTED");

    /// <summary>The tenant has not licensed the module the operation belongs to.</summary>
    public static readonly MessageCode ModuleNotLicensed = new("SEC.MODULE.NOT_LICENSED");

    /// <summary>Someone else changed the record since it was read.</summary>
    public static readonly MessageCode ConcurrencyConflict = new("PLAT.RECORD.CHANGED_BY_ANOTHER_USER");

    /// <summary>A number series has issued its last number.</summary>
    public static readonly MessageCode NumberSeriesExhausted = new("PLAT.NUMBERSERIES.EXHAUSTED");

    /// <summary>No number series line covers the document date.</summary>
    public static readonly MessageCode NumberSeriesNoLine = new("PLAT.NUMBERSERIES.NO_LINE_FOR_DATE");

    /// <summary>A document date runs backwards against a series that forbids it.</summary>
    public static readonly MessageCode NumberSeriesDateOrder = new("PLAT.NUMBERSERIES.DATE_OUT_OF_ORDER");

    /// <summary>A mandatory dimension carries no value.</summary>
    public static readonly MessageCode DimensionRequired = new("PLAT.DIMENSION.VALUE_REQUIRED");

    /// <summary>A dimension value is blocked or is not postable.</summary>
    public static readonly MessageCode DimensionValueBlocked = new("PLAT.DIMENSION.VALUE_BLOCKED");

    /// <summary>A setting was given a value that does not fit its declaration.</summary>
    public static readonly MessageCode SetupValueInvalid = new("PLAT.SETUP.VALUE_INVALID");

    /// <summary>A setting cannot be changed now that entries have been posted against it.</summary>
    public static readonly MessageCode SetupLocked = new("PLAT.SETUP.LOCKED_AFTER_POSTING");

    /// <summary>Every message the platform declares.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = PermissionDenied,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "You do not have permission to do this",
                "ليس لديك صلاحية للقيام بهذا الإجراء"),
            Detail = new LocalizedText(
                "{Operation} requires the permission {Permission} in company {Company}.",
                "يتطلب {Operation} الصلاحية {Permission} في شركة {Company}."),
            Resolution = new LocalizedText(
                "Ask an administrator to add a permission set containing {Permission} to your account for this company.",
                "اطلب من المسؤول إضافة مجموعة صلاحيات تحتوي على {Permission} إلى حسابك في هذه الشركة."),
            HelpTopic = "security/permissions",
        },
        new()
        {
            Code = NoCompanySelected,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("No company selected", "لم يتم اختيار شركة"),
            Detail = new LocalizedText(
                "This operation works inside a company, and none is selected.",
                "يعمل هذا الإجراء داخل شركة، ولم يتم اختيار أي شركة."),
            Resolution = new LocalizedText(
                "Choose a company from the company switcher and try again.",
                "اختر شركة من قائمة الشركات ثم أعد المحاولة."),
        },
        new()
        {
            Code = ModuleNotLicensed,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This module is not part of your licence", "هذه الوحدة غير مشمولة في ترخيصك"),
            Detail = new LocalizedText(
                "{Module} is installed but is not licensed for your organisation.",
                "الوحدة {Module} مثبتة ولكنها غير مرخصة لمؤسستك."),
            Resolution = new LocalizedText(
                "Contact your ASAP supplier to add {Module} to your licence.",
                "تواصل مع مورد ASAP لإضافة {Module} إلى ترخيصك."),
        },
        new()
        {
            Code = ConcurrencyConflict,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText(
                "Someone else changed this record",
                "قام مستخدم آخر بتعديل هذا السجل"),
            Detail = new LocalizedText(
                "{User} changed this record after you opened it. Saving now would discard their work.",
                "قام {User} بتعديل هذا السجل بعد فتحك له. الحفظ الآن سيؤدي إلى إلغاء تعديلاته."),
            Resolution = new LocalizedText(
                "Reload the record, check the change, and apply yours again.",
                "أعد تحميل السجل، وراجع التعديل، ثم أعد تطبيق تعديلك."),
        },
        new()
        {
            Code = NumberSeriesExhausted,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Number series has run out", "انتهت أرقام المسلسل"),
            Detail = new LocalizedText(
                "Series {Series} has issued its last number, {LastNumber}.",
                "أصدر المسلسل {Series} آخر رقم لديه، {LastNumber}."),
            Resolution = new LocalizedText(
                "Widen the range on the current line, or add a new line to series {Series}.",
                "وسّع النطاق في السطر الحالي، أو أضف سطرًا جديدًا للمسلسل {Series}."),
            HelpTopic = "setup/number-series",
        },
        new()
        {
            Code = NumberSeriesNoLine,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("No numbers defined for this date", "لا توجد أرقام معرّفة لهذا التاريخ"),
            Detail = new LocalizedText(
                "Series {Series} has no line starting on or before {DocumentDate:d}.",
                "لا يوجد سطر في المسلسل {Series} يبدأ في {DocumentDate:d} أو قبله."),
            Resolution = new LocalizedText(
                "Add a line to series {Series} starting on or before {DocumentDate:d}. "
                + "This usually means the new year has begun and no line was added for it.",
                "أضف سطرًا للمسلسل {Series} يبدأ في {DocumentDate:d} أو قبله. "
                + "يحدث هذا عادة عند بداية سنة جديدة دون إضافة سطر لها."),
            HelpTopic = "setup/number-series",
        },
        new()
        {
            Code = NumberSeriesDateOrder,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Document date is out of order", "تاريخ المستند خارج التسلسل"),
            Detail = new LocalizedText(
                "Series {Series} must be issued in date order, and {DocumentDate:d} is earlier "
                + "than {LastDate:d}, the date of the last document it numbered.",
                "يجب إصدار المسلسل {Series} حسب التسلسل الزمني، و{DocumentDate:d} أسبق من "
                + "{LastDate:d}، تاريخ آخر مستند تم ترقيمه."),
            Resolution = new LocalizedText(
                "Change the document date to {LastDate:d} or later, or use a different series.",
                "غيّر تاريخ المستند إلى {LastDate:d} أو بعده، أو استخدم مسلسلًا آخر."),
            OverridePermission = "Platform.NumberSeries.Override",
            HelpTopic = "setup/number-series",
        },
        new()
        {
            Code = DimensionRequired,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A required dimension is missing", "بُعد إلزامي مفقود"),
            Detail = new LocalizedText(
                "{Dimension} must be set on every transaction, and this one has no value for it.",
                "يجب تحديد {Dimension} في كل حركة، وهذه الحركة لا تحتوي على قيمة له."),
            Resolution = new LocalizedText(
                "Choose a value for {Dimension}, or make it optional in dimension setup.",
                "اختر قيمة لـ {Dimension}، أو اجعله اختياريًا في إعداد الأبعاد."),
            HelpTopic = "setup/dimensions",
        },
        new()
        {
            Code = DimensionValueBlocked,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("This dimension value cannot be used", "لا يمكن استخدام قيمة البُعد هذه"),
            Detail = new LocalizedText(
                "{Dimension} value {Value} is blocked or is a heading rather than a postable value.",
                "قيمة البُعد {Dimension} رقم {Value} محظورة أو أنها عنوان وليست قيمة قابلة للترحيل."),
            Resolution = new LocalizedText(
                "Choose a different value, or unblock {Value} in dimension setup.",
                "اختر قيمة أخرى، أو ألغِ حظر {Value} في إعداد الأبعاد."),
            HelpTopic = "setup/dimensions",
        },
        new()
        {
            Code = SetupValueInvalid,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That setting value is not allowed", "قيمة الإعداد هذه غير مسموح بها"),
            Detail = new LocalizedText(
                "{Setting} expects {Expected}, and '{Value}' does not fit.",
                "يتوقع الإعداد {Setting} القيمة {Expected}، و'{Value}' لا تتوافق معها."),
            Resolution = new LocalizedText(
                "Enter a value matching {Expected}.",
                "أدخل قيمة تتوافق مع {Expected}."),
        },
        new()
        {
            Code = SetupLocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "This setting can no longer be changed",
                "لم يعد بالإمكان تغيير هذا الإعداد"),
            Detail = new LocalizedText(
                "{Setting} decides how existing entries were calculated, and {Company} has posted "
                + "entries already. Changing it now would make posted figures disagree with the "
                + "rules that produced them.",
                "يحدد الإعداد {Setting} كيفية احتساب القيود الحالية، وقد قامت {Company} بترحيل "
                + "قيود بالفعل. تغييره الآن سيجعل الأرقام المرحّلة مخالفة للقواعد التي أنتجتها."),
            Resolution = new LocalizedText(
                "Set up a new company with the setting you want, or ask your ASAP partner about a "
                + "conversion.",
                "أنشئ شركة جديدة بالإعداد المطلوب، أو استشر شريك ASAP بشأن عملية تحويل."),
        },
    ];
}
