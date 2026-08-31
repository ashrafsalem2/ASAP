using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Hr;

/// <summary>
/// Everything the human resources module can refuse, and why.
/// </summary>
/// <remarks>
/// These are read by people about people, which changes how they should be written. A message
/// refusing something about somebody's pay or their leaving date will be shown to them or read
/// out to them, so it says what the rule is rather than merely that a rule was broken.
/// </remarks>
public static class HrMessages
{
    /// <summary>The employee named does not exist.</summary>
    public static readonly MessageCode EmployeeNotFound = new("HR.EMPLOYEE.NOT_FOUND");

    /// <summary>Somebody was hired into a number another employee already has.</summary>
    public static readonly MessageCode EmployeeNumberTaken = new("HR.EMPLOYEE.NUMBER_TAKEN");

    /// <summary>A leaving date before the hiring date.</summary>
    public static readonly MessageCode LeftBeforeHired = new("HR.EMPLOYEE.LEFT_BEFORE_HIRED");

    /// <summary>Somebody was recorded as having left without saying why.</summary>
    public static readonly MessageCode LeavingReasonMissing = new("HR.EMPLOYEE.NO_LEAVING_REASON");

    /// <summary>A branch assignment overlaps one that already exists.</summary>
    public static readonly MessageCode AssignmentOverlaps = new("HR.ASSIGNMENT.OVERLAPS");

    /// <summary>A branch assignment leaves a day nobody is responsible for.</summary>
    public static readonly MessageCode AssignmentLeavesGap = new("HR.ASSIGNMENT.GAP");

    /// <summary>A transfer dated before the employee was hired.</summary>
    public static readonly MessageCode AssignmentBeforeHiring = new("HR.ASSIGNMENT.BEFORE_HIRING");

    /// <summary>An employee has no branch on a day payroll needs one.</summary>
    public static readonly MessageCode NoBranchOnDate = new("HR.ASSIGNMENT.NO_BRANCH");

    /// <summary>A wage below nothing.</summary>
    public static readonly MessageCode WageNegative = new("HR.EMPLOYEE.WAGE_NEGATIVE");

    /// <summary>Two contracts would cover the same day.</summary>
    public static readonly MessageCode ContractOverlaps = new("HR.CONTRACT.OVERLAPS");

    /// <summary>A contract would start before the person was hired.</summary>
    public static readonly MessageCode ContractBeforeHiring = new("HR.CONTRACT.BEFORE_HIRING");

    /// <summary>A contract would end before it starts.</summary>
    public static readonly MessageCode ContractEndsBeforeItStarts = new("HR.CONTRACT.ENDS_BEFORE_START");

    /// <summary>A fixed-term contract has no term.</summary>
    public static readonly MessageCode ContractHasNoEnd = new("HR.CONTRACT.NO_END");

    /// <summary>An open-ended contract has been given an end.</summary>
    public static readonly MessageCode ContractShouldNotEnd = new("HR.CONTRACT.SHOULD_NOT_END");

    /// <summary>A contract pays nothing.</summary>
    public static readonly MessageCode ContractPaysNothing = new("HR.CONTRACT.PAYS_NOTHING");

    /// <summary>A contract is not there.</summary>
    public static readonly MessageCode ContractNotFound = new("HR.CONTRACT.NOT_FOUND");

    /// <summary>Somebody is being paid past the end of their contract.</summary>
    public static readonly MessageCode PaidPastContractEnd = new("HR.CONTRACT.PAID_PAST_END");

    /// <summary>Somebody has no contract, so the figures on their record were used.</summary>
    public static readonly MessageCode NoContractForPeriod = new("HR.CONTRACT.NONE_FOR_PERIOD");

    /// <summary>The leave request named does not exist.</summary>
    public static readonly MessageCode LeaveNotFound = new("HR.LEAVE.NOT_FOUND");

    /// <summary>Leave asked for that ends before it starts.</summary>
    public static readonly MessageCode LeaveEndsBeforeItStarts = new("HR.LEAVE.ENDS_BEFORE_START");

    /// <summary>Leave that covers days another request already covers.</summary>
    public static readonly MessageCode LeaveOverlaps = new("HR.LEAVE.OVERLAPS");

    /// <summary>Annual leave asked for beyond what has been earned.</summary>
    public static readonly MessageCode LeaveExceedsBalance = new("HR.LEAVE.EXCEEDS_BALANCE");

    /// <summary>Something tried to change a request that has been decided.</summary>
    public static readonly MessageCode LeaveAlreadyDecided = new("HR.LEAVE.ALREADY_DECIDED");

    /// <summary>Leave asked for outside somebody's employment.</summary>
    public static readonly MessageCode LeaveOutsideEmployment = new("HR.LEAVE.OUTSIDE_EMPLOYMENT");

    /// <summary>The payroll run named does not exist.</summary>
    public static readonly MessageCode PayrollRunNotFound = new("HR.PAYROLL.NOT_FOUND");

    /// <summary>Something tried to post a run that has already been posted.</summary>
    public static readonly MessageCode PayrollAlreadyPosted = new("HR.PAYROLL.ALREADY_POSTED");

    /// <summary>Something tried to throw away a run that has been posted.</summary>
    public static readonly MessageCode PayrollPostedCannotDiscard =
        new("HR.PAYROLL.POSTED_CANNOT_BE_DISCARDED");

    /// <summary>A draft run already covers some of the same days.</summary>
    public static readonly MessageCode PeriodAlreadyRun = new("HR.PAYROLL.PERIOD_ALREADY_RUN");

    /// <summary>A posted run already paid some of the same days.</summary>
    public static readonly MessageCode PeriodAlreadyPaid = new("HR.PAYROLL.PERIOD_ALREADY_PAID");

    /// <summary>A provision run found nothing had moved since the last one.</summary>
    public static readonly MessageCode NothingToProvision = new("HR.PROVISION.NOTHING_TO_POST");

    /// <summary>No account is set up for what the end-of-service provision is carried in.</summary>
    public static readonly MessageCode NoProvisionAccount = new("HR.SETUP.NO_PROVISION_ACCOUNT");

    /// <summary>Every message the module can raise.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = EmployeeNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such employee", "لا يوجد موظف بهذا الرقم"),
            Detail = new LocalizedText(
                "Nobody in this company is numbered {EmployeeNo}.",
                "لا يوجد في هذه الشركة موظف يحمل الرقم {EmployeeNo}."),
            Resolution = new LocalizedText(
                "Check the number against the employee list.",
                "تحقق من الرقم في قائمة الموظفين."),
            HelpTopic = "hr/employees",
        },
        new()
        {
            Code = EmployeeNumberTaken,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That number belongs to somebody", "الرقم مخصص لموظف آخر"),
            Detail = new LocalizedText(
                "{EmployeeNo} is {ExistingName}.",
                "الرقم {EmployeeNo} مخصص للموظف {ExistingName}."),
            Resolution = new LocalizedText(
                "Take the next number from the series. Reusing a leaver's number would attach one "
                + "person's service history to another, which is a matter of somebody's pay.",
                "استخدم الرقم التالي من المسلسل. فإعادة استخدام رقم موظف سابق تربط سجل خدمة شخص "
                + "بآخر، وهذا مسّ بمستحقات أحدهم."),
            HelpTopic = "hr/employees",
        },
        new()
        {
            Code = LeftBeforeHired,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("They left before they started", "تاريخ الترك قبل تاريخ التعيين"),
            Detail = new LocalizedText(
                "{EmployeeNo} is recorded as hired on {HiredOn:d} and leaving on {LeftOn:d}.",
                "الموظف {EmployeeNo} مسجّل تعيينه في {HiredOn:d} وتركه في {LeftOn:d}."),
            Resolution = new LocalizedText(
                "Correct whichever date is wrong. Service length is worked out from these two and "
                + "decides the end-of-service award.",
                "صحّح التاريخ الخاطئ. فمدة الخدمة تُحتسب منهما وتحدد مكافأة نهاية الخدمة."),
            HelpTopic = "hr/employees",
        },
        new()
        {
            Code = LeavingReasonMissing,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Say why they left", "حدّد سبب ترك العمل"),
            Detail = new LocalizedText(
                "{EmployeeNo} has a leaving date and no reason, and the two are not the same "
                + "question: a resignation is worth less than a termination, by law, and by a "
                + "great deal.",
                "للموظف {EmployeeNo} تاريخ ترك بدون سبب، وهما ليسا سؤالاً واحدًا: فالاستقالة "
                + "تختلف عن إنهاء العقد نظامًا، وبفارق كبير في المستحق."),
            Resolution = new LocalizedText(
                "Choose the reason. It is an input to the award, not a note about it.",
                "اختر السبب، فهو عنصر في احتساب المكافأة لا ملاحظة عليها."),
            HelpTopic = "hr/end-of-service",
        },
        new()
        {
            Code = ContractOverlaps,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("They cannot be on two contracts", "لا يمكن أن يكون على عقدين"),
            Detail = new LocalizedText(
                "{EmployeeNo} is already on a contract that started on {ExistingFrom:d} and still "
                + "covers {FromDate:d}. Payroll would have two wages for the same day and would pay "
                + "whichever it read first.",
                "الموظف {EmployeeNo} على عقد بدأ في {ExistingFrom:d} وما زال يغطي {FromDate:d}. "
                + "وسيجد الرواتب أجرين لليوم نفسه فيدفع أيّهما قرأه أولًا."),
            Resolution = new LocalizedText(
                "End the earlier contract the day before this one begins. Superseding it does that "
                + "for you.",
                "أنهِ العقد السابق في اليوم الذي يسبق بداية هذا العقد. وخيار الإحلال يفعل ذلك عنك."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = ContractBeforeHiring,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Before they were hired", "قبل تعيينه"),
            Detail = new LocalizedText(
                "{EmployeeNo} was hired on {HiredOn:d}, and this contract starts on {FromDate:d}.",
                "عُيّن {EmployeeNo} في {HiredOn:d}، وهذا العقد يبدأ في {FromDate:d}."),
            Resolution = new LocalizedText(
                "Start the contract on or after the hiring date, or correct the hiring date.",
                "ابدأ العقد في تاريخ التعيين أو بعده، أو صحّح تاريخ التعيين."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = ContractEndsBeforeItStarts,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("It ends before it starts", "ينتهي قبل أن يبدأ"),
            Detail = new LocalizedText(
                "This contract runs from {FromDate:d} to {ToDate:d}.",
                "هذا العقد من {FromDate:d} إلى {ToDate:d}."),
            Resolution = new LocalizedText(
                "Check the two dates. One of them is the wrong way round.",
                "راجع التاريخين. أحدهما مقلوب."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = ContractHasNoEnd,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A fixed term with no end", "مدة محددة بلا نهاية"),
            Detail = new LocalizedText(
                "This contract runs to a date, and no end date is set.",
                "هذا العقد يمتد إلى تاريخ، ولم يُحدَّد تاريخ انتهاء."),
            Resolution = new LocalizedText(
                "Set the end date, or record it as a permanent contract instead.",
                "حدّد تاريخ الانتهاء، أو سجّله عقدًا دائمًا بدلًا من ذلك."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = ContractShouldNotEnd,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A permanent contract with an end", "عقد دائم بتاريخ انتهاء"),
            Detail = new LocalizedText(
                "This contract is recorded as permanent and ends on {ToDate:d}.",
                "هذا العقد مسجَّل دائمًا وينتهي في {ToDate:d}."),
            Resolution = new LocalizedText(
                "Record it as a fixed term, or take the end date off. The difference matters to "
                + "notice and to end-of-service.",
                "سجّله محدد المدة، أو احذف تاريخ الانتهاء. فالفرق يمسّ الإشعار ومكافأة نهاية الخدمة."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = ContractPaysNothing,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A contract that pays nothing", "عقد بلا أجر"),
            Detail = new LocalizedText(
                "{EmployeeNo} would be on a basic wage of nothing from {FromDate:d}.",
                "سيكون {EmployeeNo} على أجر أساسي صفر من {FromDate:d}."),
            Resolution = new LocalizedText(
                "Enter the basic wage the contract agrees.",
                "أدخل الأجر الأساسي المتفق عليه في العقد."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = ContractNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such contract", "لا يوجد عقد بهذا"),
            Detail = new LocalizedText(
                "No contract in this company has that reference.",
                "لا يوجد عقد في هذه الشركة بهذا المعرّف."),
            Resolution = new LocalizedText(
                "Check it against the contract list.",
                "راجعه مقابل قائمة العقود."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = PaidPastContractEnd,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Paid past the end of a contract", "الدفع بعد انتهاء العقد"),
            Detail = new LocalizedText(
                "{EmployeeNo}'s contract ended on {ToDate:d} and nothing follows it, so "
                + "{UncoveredDays} day(s) of this period are covered by no contract.",
                "انتهى عقد {EmployeeNo} في {ToDate:d} ولا عقد بعده، فبقي {UncoveredDays} يومًا من "
                + "هذه الفترة بلا عقد يغطيها."),
            Resolution = new LocalizedText(
                "Renew the contract, or record them as having left. They are being paid on the "
                + "figures from their record, which nothing dates.",
                "جدّد العقد، أو سجّل انتهاء خدمته. فهو يُدفع له الآن على أرقام سجلّه، وهي أرقام لا "
                + "يؤرّخها شيء."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = NoContractForPeriod,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("No contract on record", "لا عقد مسجَّل"),
            Detail = new LocalizedText(
                "{EmployeeNo} has no contract covering this period, so the wage on their record "
                + "was used.",
                "لا عقد لـ {EmployeeNo} يغطي هذه الفترة، فاستُخدم الأجر المدوَّن في سجلّه."),
            Resolution = new LocalizedText(
                "Record their contract. The figure on the record has no date, so it will be "
                + "whatever it says today if this run is ever repeated.",
                "سجّل عقده. فالرقم في السجل بلا تاريخ، وسيكون ما يقوله اليوم إن أُعيد تشغيل هذه "
                + "الدورة يومًا."),
            HelpTopic = "hr/contracts",
        },
        new()
        {
            Code = AssignmentOverlaps,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("They cannot be in two places", "لا يمكن أن يكون في مكانين"),
            Detail = new LocalizedText(
                "{EmployeeNo} is already assigned to a branch from {ExistingFrom:d}, and this "
                + "assignment starts on {FromDate:d}. Payroll would charge those days twice.",
                "الموظف {EmployeeNo} مُسند بالفعل إلى فرع من {ExistingFrom:d}، وهذا الإسناد يبدأ "
                + "في {FromDate:d}. وسيُحمّل الرواتب تلك الأيام مرتين."),
            Resolution = new LocalizedText(
                "End the earlier assignment the day before this one begins.",
                "أنهِ الإسناد السابق في اليوم الذي يسبق بداية هذا الإسناد."),
            HelpTopic = "hr/branches",
        },
        new()
        {
            Code = AssignmentLeavesGap,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("A day belongs to nobody", "يوم بلا جهة"),
            Detail = new LocalizedText(
                "{EmployeeNo} has no branch between {GapFrom:d} and {GapTo:d}. Those days are "
                + "worked and paid for, and no branch would carry the cost.",
                "الموظف {EmployeeNo} بلا فرع بين {GapFrom:d} و {GapTo:d}. وهي أيام عمل مدفوعة "
                + "الأجر ولن يتحمّل تكلفتها أي فرع."),
            Resolution = new LocalizedText(
                "Start this assignment on {GapFrom:d}, or extend the previous one to cover the gap.",
                "ابدأ هذا الإسناد في {GapFrom:d}، أو مدّد الإسناد السابق ليغطي الفجوة."),
            HelpTopic = "hr/branches",
        },
        new()
        {
            Code = AssignmentBeforeHiring,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Before they were hired", "قبل تاريخ التعيين"),
            Detail = new LocalizedText(
                "The assignment starts on {FromDate:d} and {EmployeeNo} was hired on {HiredOn:d}.",
                "يبدأ الإسناد في {FromDate:d} بينما عُيّن الموظف {EmployeeNo} في {HiredOn:d}."),
            Resolution = new LocalizedText(
                "Start it on the hiring date or later.",
                "ابدأه في تاريخ التعيين أو بعده."),
            HelpTopic = "hr/branches",
        },
        new()
        {
            Code = NoBranchOnDate,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No branch to charge", "لا يوجد فرع لتحميل التكلفة"),
            Detail = new LocalizedText(
                "{EmployeeNo} has no branch on {OnDate:d}, so there is nowhere to charge the day.",
                "الموظف {EmployeeNo} بلا فرع في {OnDate:d}، فلا توجد جهة لتحميل تكلفة اليوم."),
            Resolution = new LocalizedText(
                "Assign them to a branch covering {OnDate:d} before running payroll.",
                "أسنده إلى فرع يغطي {OnDate:d} قبل تشغيل الرواتب."),
            HelpTopic = "hr/branches",
        },
        new()
        {
            Code = WageNegative,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A wage cannot be negative", "لا يمكن أن يكون الأجر سالبًا"),
            Detail = new LocalizedText(
                "{EmployeeNo} was given a basic wage of {BasicWage:N2} and allowances of "
                + "{Allowances:N2}.",
                "أُدخل للموظف {EmployeeNo} أجر أساسي {BasicWage:N2} وبدلات {Allowances:N2}."),
            Resolution = new LocalizedText(
                "Enter what they are paid. A deduction is recorded against a payroll run, not "
                + "against the contract.",
                "أدخل ما يتقاضاه فعلاً. فالاستقطاع يُسجَّل على مسيّر الرواتب لا على العقد."),
            HelpTopic = "hr/employees",
        },
        new()
        {
            Code = LeaveNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such leave request", "لا يوجد طلب إجازة بهذا الرقم"),
            Detail = new LocalizedText(
                "No leave request in this company is numbered {RequestNo}.",
                "لا يوجد في هذه الشركة طلب إجازة يحمل الرقم {RequestNo}."),
            Resolution = new LocalizedText(
                "Check the number against the leave register.",
                "تحقق من الرقم في سجل الإجازات."),
            HelpTopic = "hr/leave",
        },
        new()
        {
            Code = LeaveEndsBeforeItStarts,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("It ends before it starts", "تاريخ النهاية قبل البداية"),
            Detail = new LocalizedText(
                "The leave runs from {FromDate:d} to {ToDate:d}.",
                "الإجازة من {FromDate:d} إلى {ToDate:d}."),
            Resolution = new LocalizedText(
                "Correct whichever date is wrong. A single day is entered with the same date twice.",
                "صحّح التاريخ الخاطئ. واليوم الواحد يُدخل بنفس التاريخ في الحقلين."),
            HelpTopic = "hr/leave",
        },
        new()
        {
            Code = LeaveOverlaps,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("They are already away then", "الموظف في إجازة في هذه الأيام"),
            Detail = new LocalizedText(
                "{EmployeeNo} already has {ExistingNo} from {ExistingFrom:d} to {ExistingTo:d}, "
                + "which covers some of the same days. Counted twice, those days come off the "
                + "balance twice and off the wage twice.",
                "لدى الموظف {EmployeeNo} الطلب {ExistingNo} من {ExistingFrom:d} إلى {ExistingTo:d}، "
                + "وهو يغطي بعض الأيام نفسها. واحتسابها مرتين يخصمها من الرصيد مرتين ومن الأجر "
                + "مرتين."),
            Resolution = new LocalizedText(
                "Change the dates, or cancel {ExistingNo} first.",
                "غيّر التواريخ، أو ألغِ الطلب {ExistingNo} أولاً."),
            HelpTopic = "hr/leave",
        },
        new()
        {
            Code = LeaveExceedsBalance,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("More than they have earned", "أكثر من الرصيد المستحق"),
            Detail = new LocalizedText(
                "{EmployeeNo} is asking for {Days:N1} days and has {BalanceDays:N1} earned by "
                + "{ToDate:d}.",
                "يطلب الموظف {EmployeeNo} عدد {Days:N1} يومًا، ورصيده المستحق حتى {ToDate:d} هو "
                + "{BalanceDays:N1} يومًا."),
            Resolution = new LocalizedText(
                "Granted anyway, it is leave taken in advance and the balance goes negative until "
                + "it is earned back. That is a decision somebody should make on purpose rather "
                + "than discover at the end of the year.",
                "إن مُنحت فهي إجازة مقدَّمة ويصبح الرصيد سالبًا حتى تُستحق. وهذا قرار ينبغي أن "
                + "يُتخذ عن قصد لا أن يُكتشف في نهاية العام."),
            HelpTopic = "hr/leave",
        },
        new()
        {
            Code = LeaveAlreadyDecided,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("This has been decided", "تم البتّ في هذا الطلب"),
            Detail = new LocalizedText(
                "{RequestNo} is {Status}. What was decided about somebody's time off is an answer "
                + "they were given, and editing it afterwards changes the record of what they "
                + "were told.",
                "الطلب {RequestNo} حالته {Status}. وما تقرر بشأن إجازة الموظف جواب أُبلغ به، "
                + "وتعديله لاحقًا يغيّر سجل ما قيل له."),
            Resolution = new LocalizedText(
                "Cancel it and raise another, so both the decision and the change to it are on "
                + "the record.",
                "ألغه وأنشئ طلبًا آخر، ليبقى القرار وتعديله كلاهما في السجل."),
            HelpTopic = "hr/leave",
        },
        new()
        {
            Code = LeaveOutsideEmployment,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("They were not employed then", "خارج مدة العمل"),
            Detail = new LocalizedText(
                "The leave runs from {FromDate:d} to {ToDate:d}, and {EmployeeNo} was hired on "
                + "{HiredOn:d}{LeftClause}.",
                "الإجازة من {FromDate:d} إلى {ToDate:d}، والموظف {EmployeeNo} عُيّن في "
                + "{HiredOn:d}{LeftClause}."),
            Resolution = new LocalizedText(
                "Leave is earned by working, so it can only be taken over days somebody was here.",
                "الإجازة تُستحق بالعمل، فلا تُؤخذ إلا عن أيام كان فيها الموظف على رأس العمل."),
            HelpTopic = "hr/leave",
        },
        new()
        {
            Code = PayrollRunNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such payroll run", "لا يوجد مسيّر رواتب بهذا الرقم"),
            Detail = new LocalizedText(
                "No payroll run in this company is numbered {RunNo}.",
                "لا يوجد في هذه الشركة مسيّر رواتب يحمل الرقم {RunNo}."),
            Resolution = new LocalizedText(
                "Check the number against the payroll list.",
                "تحقق من الرقم في قائمة مسيّرات الرواتب."),
            HelpTopic = "hr/payroll",
        },
        new()
        {
            Code = PayrollAlreadyPosted,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This run has been posted", "تم ترحيل هذا المسيّر"),
            Detail = new LocalizedText(
                "{RunNo} posted as transaction {TransactionNo}. What people are owed is already a "
                + "liability the company carries, and posting it again would owe it twice.",
                "رُحّل المسيّر {RunNo} ضمن الحركة {TransactionNo}. وما يستحقه الموظفون أصبح التزامًا "
                + "قائمًا على الشركة، وترحيله ثانية يجعله مستحقًا مرتين."),
            Resolution = new LocalizedText(
                "Correct it with an adjustment run rather than by posting this one again.",
                "صحّحه بمسيّر تسوية بدلاً من إعادة ترحيل هذا المسيّر."),
            HelpTopic = "hr/payroll",
        },
        new()
        {
            Code = PayrollPostedCannotDiscard,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A posted run is not thrown away", "المسيّر المرحّل لا يُحذف"),
            Detail = new LocalizedText(
                "{RunNo} was posted as transaction {TransactionNo}. Removing it would leave the "
                + "ledger holding what it paid and nothing saying who it was paid to.",
                "رُحّل المسيّر {RunNo} ضمن الحركة {TransactionNo}. وحذفه يترك في الدفاتر مبلغًا "
                + "مدفوعًا دون ما يبيّن لمن دُفع."),
            Resolution = new LocalizedText(
                "Reverse transaction {TransactionNo} instead. A reversal leaves both the payment "
                + "and its undoing on the record, which is what somebody asking next year what "
                + "happened needs to see.",
                "اعكس الحركة {TransactionNo} بدلاً من ذلك. فالعكس يُبقي القيد وإلغاءه معًا في "
                + "السجل، وهو ما يحتاج أن يراه من يسأل بعد عام عمّا جرى."),
            HelpTopic = "hr/payroll",
        },
        new()
        {
            Code = PeriodAlreadyRun,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("Another run covers these days", "مسيّر آخر يغطي هذه الأيام"),
            Detail = new LocalizedText(
                "{RunNo} is a draft covering {ExistingFrom:d} to {ExistingTo:d}, which overlaps "
                + "this period.",
                "المسيّر {RunNo} مسودة تغطي من {ExistingFrom:d} إلى {ExistingTo:d}، وهي فترة "
                + "متداخلة مع هذه."),
            Resolution = new LocalizedText(
                "Two drafts for the same days are not wrong by themselves — only one of them can "
                + "be posted. Decide which, before somebody else has to.",
                "وجود مسودتين لنفس الأيام ليس خطأً بذاته، لكن لا يجوز ترحيل إلا واحدة منهما. "
                + "فاحسم أيّهما قبل أن يقرر ذلك غيرك."),
            HelpTopic = "hr/payroll",
        },
        new()
        {
            Code = PeriodAlreadyPaid,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("These days have been paid", "هذه الأيام مدفوعة بالفعل"),
            Detail = new LocalizedText(
                "{RunNo} was posted as transaction {TransactionNo} and covers {ExistingFrom:d} to "
                + "{ExistingTo:d}. Posting this one as well would owe everybody for the "
                + "overlapping days twice.",
                "رُحّل المسيّر {RunNo} ضمن الحركة {TransactionNo} وهو يغطي من {ExistingFrom:d} "
                + "إلى {ExistingTo:d}. وترحيل هذا المسيّر أيضًا يجعل الأيام المتداخلة مستحقة "
                + "مرتين لكل الموظفين."),
            Resolution = new LocalizedText(
                "Narrow this run to the days not already paid. If it is a correction to what was "
                + "paid, that is a real thing to post, and somebody with the override permission "
                + "may post it with a reason.",
                "اقصر هذا المسيّر على الأيام غير المدفوعة. وإن كان تصحيحًا لما دُفع فهو ترحيل "
                + "مشروع، ويمكن لمن يملك صلاحية التجاوز ترحيله مع بيان السبب."),
            OverridePermission = "Hr.Payroll.Override",
            HelpTopic = "hr/payroll",
        },
        new()
        {
            Code = NothingToProvision,
            Severity = MessageSeverity.Information,
            Title = new LocalizedText("Nothing has changed", "لا تغيير"),
            Detail = new LocalizedText(
                "What the company owes has not moved since the last run: still {Amount:N2}.",
                "لم يتغيّر ما تدين به الشركة منذ آخر تشغيل: لا يزال {Amount:N2}."),
            Resolution = new LocalizedText(
                "Nothing to do. This is not a failure — the figure this run computed is the "
                + "figure already carried in the ledger.",
                "لا حاجة لأي إجراء. فهذا ليس خطأً، بل إن الرقم الذي احتسبه هذا التشغيل هو ذاته "
                + "المرحّل بالفعل في دفتر الأستاذ."),
            HelpTopic = "hr/provisions",
        },
        new()
        {
            Code = NoProvisionAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText(
                "Nowhere to carry the provision",
                "لا يوجد حساب لمخصص نهاية الخدمة"),
            Detail = new LocalizedText(
                "No account is set up for {SettingKey}, so a provision of {Amount:N2} has nowhere "
                + "to go.",
                "لا يوجد حساب معرَّف للإعداد {SettingKey}، فمخصص بقيمة {Amount:N2} بلا وجهة."),
            Resolution = new LocalizedText(
                "Set {SettingKey} in setup. What the company will owe its staff is a liability it "
                + "carries now, not a cost that appears on the day somebody leaves.",
                "حدّد الإعداد {SettingKey}. فما تدين به الشركة لموظفيها التزام قائم الآن، لا "
                + "تكلفة تظهر يوم ترك العمل."),
            HelpTopic = "hr/setup",
        },
    ];
}
