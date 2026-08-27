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
            Code = NoProvisionAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText(
                "Nowhere to carry the provision",
                "لا يوجد حساب لمخصص نهاية الخدمة"),
            Detail = new LocalizedText(
                "No end-of-service account is set up, so a provision of {Amount:N2} has nowhere "
                + "to go.",
                "لا يوجد حساب لنهاية الخدمة، فمخصص بقيمة {Amount:N2} بلا وجهة."),
            Resolution = new LocalizedText(
                "Set Hr.Posting.EndOfServiceAccount in setup. What the company will owe its staff "
                + "when they leave is a liability it carries now, not a cost that appears on the "
                + "day somebody resigns.",
                "حدّد الإعداد Hr.Posting.EndOfServiceAccount. فما تدين به الشركة لموظفيها عند "
                + "تركهم العمل التزام قائم الآن، لا تكلفة تظهر يوم الاستقالة."),
            HelpTopic = "hr/setup",
        },
    ];
}
