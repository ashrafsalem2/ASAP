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

    /// <summary>The series named does not exist here, or has been switched off.</summary>
    public static readonly MessageCode NumberSeriesUnavailable = new("PLAT.NUMBERSERIES.UNAVAILABLE");

    /// <summary>A series is close to the end of its range.</summary>
    public static readonly MessageCode NumberSeriesRunningLow = new("PLAT.NUMBERSERIES.RUNNING_LOW");

    /// <summary>Someone typed a number into a series that issues its own.</summary>
    public static readonly MessageCode NumberSeriesManualNotAllowed
        = new("PLAT.NUMBERSERIES.MANUAL_NOT_ALLOWED");

    /// <summary>A typed number falls at or below one the series has already issued.</summary>
    public static readonly MessageCode NumberSeriesNumberInUse = new("PLAT.NUMBERSERIES.NUMBER_IN_USE");

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

    /// <summary>The user named does not exist.</summary>
    public static readonly MessageCode UserNotFound = new("PLAT.USER.NOT_FOUND");

    /// <summary>Somebody already signs in with that name.</summary>
    public static readonly MessageCode UserNameTaken = new("PLAT.USER.NAME_TAKEN");

    /// <summary>An administrator tried to turn off their own account.</summary>
    public static readonly MessageCode CannotDisableSelf = new("PLAT.USER.CANNOT_DISABLE_SELF");

    /// <summary>A change would leave nobody able to administer the installation.</summary>
    public static readonly MessageCode LastAdministrator = new("PLAT.USER.LAST_ADMINISTRATOR");

    /// <summary>A password too short to be worth having.</summary>
    public static readonly MessageCode PasswordTooShort = new("PLAT.PASSWORD.TOO_SHORT");

    /// <summary>The current password given does not match.</summary>
    public static readonly MessageCode PasswordWrong = new("PLAT.PASSWORD.WRONG");

    /// <summary>The permission set named does not exist.</summary>
    public static readonly MessageCode PermissionSetNotFound = new("PLAT.PERMISSIONSET.NOT_FOUND");

    /// <summary>A permission set already has that code.</summary>
    public static readonly MessageCode PermissionSetCodeTaken = new("PLAT.PERMISSIONSET.CODE_TAKEN");

    /// <summary>Something tried to change a set ASAP maintains.</summary>
    public static readonly MessageCode PermissionSetIsSystem = new("PLAT.PERMISSIONSET.SYSTEM_DEFINED");

    /// <summary>Something tried to remove a set somebody is still assigned.</summary>
    public static readonly MessageCode PermissionSetInUse = new("PLAT.PERMISSIONSET.IN_USE");

    /// <summary>A set names a permission no module declares.</summary>
    public static readonly MessageCode PermissionUnknown = new("PLAT.PERMISSION.UNKNOWN");

    /// <summary>A setting nobody declares.</summary>
    public static readonly MessageCode SetupUnknownKey = new("PLAT.SETUP.UNKNOWN_KEY");

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
            Code = NumberSeriesUnavailable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That number series is not available", "المسلسل غير متاح"),
            Detail = new LocalizedText(
                "No active series in this company is coded {Series}.",
                "لا يوجد مسلسل فعّال في هذه الشركة بالرمز {Series}."),
            Resolution = new LocalizedText(
                "Create series {Series} in number series setup, or switch it back on if it was "
                + "deactivated. Documents that need a number cannot be raised until it exists.",
                "أنشئ المسلسل {Series} في إعداد المسلسلات، أو أعد تفعيله إن كان معطّلاً. "
                + "لا يمكن إنشاء المستندات التي تحتاج رقمًا قبل وجوده."),
            HelpTopic = "setup/number-series",
        },
        new()
        {
            Code = NumberSeriesRunningLow,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Number series is running low", "أوشك المسلسل على النفاد"),
            Detail = new LocalizedText(
                "Series {Series} has {Remaining} number(s) left.",
                "تبقّى في المسلسل {Series} {Remaining} رقم."),
            Resolution = new LocalizedText(
                "Widen the range or add a new line to series {Series} before it runs out. Said now "
                + "rather than at the moment it stops, which is usually mid-trading.",
                "وسّع النطاق أو أضف سطرًا جديدًا للمسلسل {Series} قبل نفاده. يُقال هذا الآن بدلاً "
                + "من لحظة التوقف التي تأتي عادة في وقت العمل."),
            HelpTopic = "setup/number-series",
        },
        new()
        {
            Code = NumberSeriesManualNotAllowed,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This number cannot be typed", "لا يمكن كتابة هذا الرقم"),
            Detail = new LocalizedText(
                "Series {Series} issues its own numbers, so {Number} cannot be entered by hand.",
                "المسلسل {Series} يصدر أرقامه بنفسه، لذا لا يمكن إدخال {Number} يدويًا."),
            Resolution = new LocalizedText(
                "Leave the number blank and let ASAP issue the next one, or allow manual entry on "
                + "series {Series}. A statutory sequence should stay closed to typing.",
                "اترك الرقم فارغًا ليصدره النظام، أو اسمح بالإدخال اليدوي في المسلسل {Series}. "
                + "يُفضّل إبقاء التسلسل النظامي مغلقًا أمام الإدخال اليدوي."),
            HelpTopic = "setup/number-series",
        },
        new()
        {
            Code = NumberSeriesNumberInUse,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That number has already been issued", "هذا الرقم صدر من قبل"),
            Detail = new LocalizedText(
                "Series {Series} has already reached {LastNumber}, so {Number} is behind it.",
                "وصل المسلسل {Series} إلى {LastNumber}، لذا فإن {Number} يسبقه."),
            Resolution = new LocalizedText(
                "Use a number after {LastNumber}, or leave it blank and let ASAP issue the next one.",
                "استخدم رقمًا بعد {LastNumber}، أو اتركه فارغًا ليصدره النظام."),
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
            Code = UserNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such user", "لا يوجد مستخدم بهذا الاسم"),
            Detail = new LocalizedText(
                "No user account here is called {UserName}.",
                "لا يوجد هنا حساب مستخدم باسم {UserName}."),
            Resolution = new LocalizedText(
                "Check the name against the user list.",
                "تحقق من الاسم في قائمة المستخدمين."),
            HelpTopic = "platform/users",
        },
        new()
        {
            Code = UserNameTaken,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That name is taken", "الاسم مستخدم بالفعل"),
            Detail = new LocalizedText(
                "{UserName} already belongs to {ExistingName}. Two accounts signing in under one "
                + "name would make the audit log unable to say which of them did anything.",
                "الاسم {UserName} يخص بالفعل {ExistingName}. ووجود حسابين بنفس اسم الدخول يجعل "
                + "سجل التدقيق عاجزًا عن تحديد أيهما قام بالإجراء."),
            Resolution = new LocalizedText(
                "Choose another. A name is not freed by turning an account off; it is freed by "
                + "renaming the account that holds it.",
                "اختر اسمًا آخر. فتعطيل الحساب لا يحرّر اسمه، وإنما يتحرر بإعادة تسمية الحساب "
                + "الذي يحمله."),
            HelpTopic = "platform/users",
        },
        new()
        {
            Code = CannotDisableSelf,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("You cannot turn off your own account", "لا يمكنك تعطيل حسابك"),
            Detail = new LocalizedText(
                "{UserName} is the account you are signed in with.",
                "الحساب {UserName} هو الحساب الذي سجّلت الدخول به."),
            Resolution = new LocalizedText(
                "Ask another administrator to do it. The rule exists so that nobody removes their "
                + "own way back in and then discovers it.",
                "اطلب من مسؤول آخر القيام بذلك. فالقاعدة موجودة كي لا يقطع أحد طريق عودته ثم "
                + "يكتشف ذلك."),
            HelpTopic = "platform/users",
        },
        new()
        {
            Code = LastAdministrator,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "Somebody has to be able to administer this",
                "لا بد أن يبقى من يدير النظام"),
            Detail = new LocalizedText(
                "{UserName} is the last account able to administer users and permissions. Making "
                + "this change would leave the installation with nobody who can grant anybody "
                + "anything, including the right to undo it.",
                "الحساب {UserName} هو آخر حساب يستطيع إدارة المستخدمين والصلاحيات. وإجراء هذا "
                + "التغيير يترك النظام بلا من يملك منح أي صلاحية لأحد، بما في ذلك صلاحية التراجع "
                + "عن هذا التغيير."),

            // No override. There is no version of this that anybody recovers from without a
            // database, which is not a support call worth designing in.
            Resolution = new LocalizedText(
                "Give somebody else the administrator set first, then come back to this.",
                "امنح مستخدمًا آخر مجموعة صلاحيات المسؤول أولاً، ثم عد إلى هذا الإجراء."),
            HelpTopic = "platform/users",
        },
        new()
        {
            Code = PasswordTooShort,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That password is too short", "كلمة المرور قصيرة جدًا"),
            Detail = new LocalizedText(
                "A password must be at least {Minimum} characters, and this one is {Length}.",
                "يجب ألا تقل كلمة المرور عن {Minimum} حرفًا، وهذه {Length}."),
            Resolution = new LocalizedText(
                "Length is what makes a password hard to guess; a longer ordinary phrase beats a "
                + "short one with symbols in it.",
                "الطول هو ما يصعّب تخمين كلمة المرور، وعبارة عادية طويلة أفضل من كلمة قصيرة "
                + "مليئة بالرموز."),
            HelpTopic = "platform/users",
        },
        new()
        {
            Code = PasswordWrong,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That is not your current password", "كلمة المرور الحالية غير صحيحة"),
            Detail = new LocalizedText(
                "The current password given does not match the one on the account.",
                "كلمة المرور الحالية المُدخلة لا تطابق المسجّلة على الحساب."),
            Resolution = new LocalizedText(
                "Try again, or ask an administrator to reset it. Somebody who could change a "
                + "password without knowing the old one could take an account left signed in.",
                "حاول مرة أخرى أو اطلب من المسؤول إعادة التعيين. فمن يستطيع تغيير كلمة المرور "
                + "دون معرفة القديمة يستطيع الاستيلاء على حساب تُرك مفتوحًا."),
            HelpTopic = "platform/users",
        },
        new()
        {
            Code = PermissionSetNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such permission set", "لا توجد مجموعة صلاحيات بهذا الرمز"),
            Detail = new LocalizedText(
                "No permission set here is coded {Code}.",
                "لا توجد هنا مجموعة صلاحيات بالرمز {Code}."),
            Resolution = new LocalizedText(
                "Check the code against the permission set list.",
                "تحقق من الرمز في قائمة مجموعات الصلاحيات."),
            HelpTopic = "platform/permissions",
        },
        new()
        {
            Code = PermissionSetCodeTaken,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That code is taken", "الرمز مستخدم بالفعل"),
            Detail = new LocalizedText(
                "{Code} already belongs to {ExistingName}.",
                "الرمز {Code} يخص بالفعل {ExistingName}."),
            Resolution = new LocalizedText(
                "Choose another code, or edit the set that has it.",
                "اختر رمزًا آخر، أو عدّل المجموعة التي تحمله."),
            HelpTopic = "platform/permissions",
        },
        new()
        {
            Code = PermissionSetIsSystem,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("ASAP maintains that set", "هذه المجموعة يحافظ عليها النظام"),
            Detail = new LocalizedText(
                "{Code} is kept in step with what the installed modules declare, so an edit to it "
                + "would be undone the next time ASAP starts.",
                "تُحدَّث المجموعة {Code} تلقائيًا لتواكب ما تعلنه الوحدات المثبتة، فأي تعديل عليها "
                + "سيُلغى عند التشغيل التالي."),
            Resolution = new LocalizedText(
                "Copy it to a set of your own and change that. A set you made is yours; this one "
                + "belongs to the software.",
                "انسخها إلى مجموعة خاصة بك وعدّل تلك. فالمجموعة التي تنشئها ملكك، وهذه تخص "
                + "البرنامج."),
            HelpTopic = "platform/permissions",
        },
        new()
        {
            Code = PermissionSetInUse,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Somebody is still using that set", "المجموعة ما زالت مسندة"),
            Detail = new LocalizedText(
                "{Code} is assigned to {Count} people. Removing it would take away whatever it "
                + "granted them, without saying which of them lost what.",
                "المجموعة {Code} مسندة إلى {Count} من المستخدمين. وحذفها يسحب منهم ما تمنحه دون "
                + "بيان ما فقده كل منهم."),
            Resolution = new LocalizedText(
                "Take it off those accounts first, so each change is visible on the account it "
                + "affects.",
                "أزلها من تلك الحسابات أولاً، ليظهر كل تغيير على الحساب الذي يخصه."),
            HelpTopic = "platform/permissions",
        },
        new()
        {
            Code = PermissionUnknown,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such permission", "لا توجد صلاحية بهذا الاسم"),
            Detail = new LocalizedText(
                "No module loaded here declares {Permission}.",
                "لا توجد بين الوحدات المحمّلة هنا صلاحية باسم {Permission}."),
            Resolution = new LocalizedText(
                "Choose from the permission list. A permission belonging to a module that is not "
                + "installed grants nothing, and a set holding one would look like it did.",
                "اختر من قائمة الصلاحيات. فالصلاحية التابعة لوحدة غير مثبّتة لا تمنح شيئًا، "
                + "ومجموعة تحتويها ستبدو وكأنها تمنح."),
            HelpTopic = "platform/permissions",
        },
        new()
        {
            Code = SetupUnknownKey,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such setting", "لا يوجد إعداد بهذا الاسم"),
            Detail = new LocalizedText(
                "Nothing loaded here declares a setting called {Setting}.",
                "لا يوجد بين ما هو محمَّل هنا إعداد باسم {Setting}."),
            Resolution = new LocalizedText(
                "Check the name against the setup screen. A setting belonging to a module that is "
                + "not installed does not exist on this installation, which is a different thing "
                + "from being unset.",
                "تحقق من الاسم في شاشة الإعدادات. فالإعداد التابع لوحدة غير مثبّتة لا وجود له في "
                + "هذا التركيب، وهذا يختلف عن كونه غير محدَّد."),
            HelpTopic = "platform/setup",
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
