using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Modules.Hr;

/// <summary>
/// The human resources module: employees, where they work, and what they are owed.
/// </summary>
public sealed class HrModule : IAsapModule, ISyncContributor
{
    /// <summary>The module identifier used in every HR permission and setting key.</summary>
    public const string Id = "Hr";

    /// <inheritdoc />
    public string ModuleId => Id;

    /// <inheritdoc />
    public LocalizedText DisplayName => new("Human resources", "الموارد البشرية");

    /// <inheritdoc />
    public LocalizedText Description => new(
        "Employees, the branches they have worked at and when, and the leave and end-of-service "
        + "entitlements the law says they have earned.",
        "الموظفون، والفروع التي عملوا بها ومتى، ومستحقات الإجازات ونهاية الخدمة التي يقرّها النظام.");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <summary>
    /// HR sits above Finance and touches nothing else.
    /// </summary>
    /// <remarks>
    /// Payroll posts to the ledger and the end-of-service provision is a liability the company
    /// carries, so Finance is a real dependency. Stock is not: nobody is paid in inventory.
    /// See docs/architecture/module-dependencies.md.
    /// </remarks>
    public IReadOnlyCollection<string> DependsOn =>
    [
        Platform.Core.Modules.PlatformModule.Id,
        Modules.Finance.FinanceModule.Id,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> Messages => HrMessages.All;

    /// <summary>
    /// Positions travel down to branches; nothing about a person does.
    /// </summary>
    /// <remarks>
    /// A branch needs the job titles to fill a rota. It does not need everybody's identity number
    /// and wage, and replicating those to every shop in the country to save a lookup would be a
    /// poor trade for the one time a laptop is stolen.
    /// </remarks>
    public IReadOnlyCollection<SyncEntityDescriptor> SyncEntities =>
    [
        new("Hr.Position", typeof(People.Position), SyncDirection.Down, Id),
    ];

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<People.EmployeeService>();
        services.AddScoped<Payroll.PayrollService>();
        services.AddScoped<Leave.LeaveService>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            Id, "Employee", PermissionAction.Read,
            new LocalizedText("View employees", "عرض الموظفين"),
            new LocalizedText(
                "See the staff list, where people work and what they do. Not what they are paid.",
                "الاطلاع على قائمة الموظفين وأماكن عملهم ومهامهم، دون رواتبهم.")),

        PermissionDescriptor.Define(
            Id, "Employee", PermissionAction.Create,
            new LocalizedText("Hire people", "تعيين موظفين"),
            implies: [$"{Id}.Employee.Read"]),

        PermissionDescriptor.Define(
            Id, "Employee", PermissionAction.Update,
            new LocalizedText("Change employee records", "تعديل بيانات الموظفين"),
            implies: [$"{Id}.Employee.Read"]),

        PermissionDescriptor.Define(
            Id, "Wage", PermissionAction.Read,
            new LocalizedText("See what people are paid", "الاطلاع على الرواتب"),
            new LocalizedText(
                "Separate from viewing the staff list on purpose. A supervisor needs to know who "
                + "works for them; far fewer people need to know what each of them earns.",
                "منفصلة عن عرض قائمة الموظفين عمدًا. فالمشرف يحتاج معرفة من يعمل لديه، بينما لا "
                + "يحتاج معرفة رواتبهم إلا قلّة."),
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Wage", PermissionAction.Update,
            new LocalizedText("Change what people are paid", "تعديل الرواتب"),
            implies: [$"{Id}.Wage.Read", $"{Id}.Employee.Read"],
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Payroll", PermissionAction.Override,
            new LocalizedText("Post payroll past a protection", "ترحيل الرواتب رغم الاعتراض"),
            new LocalizedText(
                "Chiefly: post a run covering days a posted run already paid. A correction run "
                + "is a real thing and does exactly that, so this cannot simply be forbidden — "
                + "but paying a month twice is not recoverable by noticing later, so it is not "
                + "something anybody running payroll should be able to do without saying why.",
                "وأبرزها ترحيل مسيّر يغطي أيامًا سبق دفعها بمسيّر مرحّل. فمسيّر التصحيح أمر "
                + "مشروع ويفعل ذلك تمامًا، فلا يصح منعه منعًا مطلقًا — لكن دفع الشهر مرتين لا "
                + "يُتدارك بملاحظته لاحقًا، فلا ينبغي أن يقدر عليه كل من يشغّل الرواتب دون بيان "
                + "السبب."),
            isSensitive: true),

        PermissionDescriptor.Define(
            Id, "Leave", PermissionAction.Read,
            new LocalizedText("See leave", "الاطلاع على الإجازات"),
            new LocalizedText(
                "Who is away, when, and what is left of their entitlement.",
                "من هو في إجازة ومتى، وما تبقى من رصيده."),
            implies: [$"{Id}.Employee.Read"]),

        PermissionDescriptor.Define(
            Id, "Leave", PermissionAction.Create,
            new LocalizedText("Ask for leave", "طلب إجازة"),
            implies: [$"{Id}.Leave.Read"]),

        PermissionDescriptor.Define(
            Id, "Leave", PermissionAction.Approve,
            new LocalizedText("Decide on leave", "البتّ في طلبات الإجازة"),
            new LocalizedText(
                "Separate from asking for it on purpose. Granting leave commits the company to "
                + "paying for days nobody works and to being short-staffed on them, which is not "
                + "a decision the person going away should be making alone.",
                "منفصلة عن طلبها عمدًا. فمنح الإجازة يُلزم الشركة بدفع أيام لا عمل فيها وبنقص "
                + "العمالة خلالها، وليس قرارًا يخص من سيغيب وحده."),
            implies: [$"{Id}.Leave.Read"]),

        PermissionDescriptor.Define(
            Id, "Report", PermissionAction.Read,
            new LocalizedText("Run staff reports", "تشغيل تقارير الموظفين"),
            new LocalizedText(
                "Headcount, turnover, cost per branch and what the company owes in unused leave "
                + "and end-of-service.",
                "أعداد الموظفين ودورانهم وتكلفتهم لكل فرع، وما تدين به الشركة من إجازات غير "
                + "مستخدمة ومكافآت نهاية خدمة.")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = $"{Id}.Posting.EndOfServiceAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("End of service provision", "مخصص نهاية الخدمة"),
            Description = new LocalizedText(
                "Where the liability for what staff have earned is carried. What the company will "
                + "owe when people leave is a liability now, not a cost that appears on the day "
                + "somebody resigns — and a company that only recognises it then is one whose "
                + "profit is overstated every year until it is not.",
                "الحساب الذي يُحمَّل عليه ما استحقه الموظفون. فما ستدين به الشركة عند تركهم العمل "
                + "التزام قائم الآن لا تكلفة تظهر يوم الاستقالة، والشركة التي لا تعترف به إلا "
                + "حينها تظهر أرباحها مبالغًا فيها كل عام حتى تنكشف."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "2500",
            RequiresPermission = $"{Id}.Wage.Update",
            HelpTopic = "hr/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.WageAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Wages and salaries", "الأجور والرواتب"),
            Description = new LocalizedText(
                "Where the cost of employing people is charged. Debited per branch, so the shop "
                + "that had somebody carries their wage for the days they were there.",
                "الحساب الذي تُحمَّل عليه تكلفة الموظفين. ويُخصم لكل فرع، فيتحمّل الفرع الذي عمل "
                + "به الموظف أجره عن الأيام التي قضاها فيه."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "6100",
            RequiresPermission = $"{Id}.Wage.Update",
            HelpTopic = "hr/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.PayableAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Net pay owed", "صافي الرواتب المستحقة"),
            Description = new LocalizedText(
                "Where what people are owed sits between the payroll being posted and the money "
                + "leaving the bank. Posting a payroll does not pay anybody; conflating the two "
                + "is how a company comes to believe it has paid staff it has not.",
                "الحساب الذي يُقيَّد فيه المستحق للموظفين بين ترحيل المسيّر وخروج المبلغ من البنك. "
                + "فترحيل الرواتب ليس صرفًا لها، والخلط بينهما يجعل الشركة تظن أنها دفعت لموظفين "
                + "لم تدفع لهم."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "2400",
            RequiresPermission = $"{Id}.Wage.Update",
            HelpTopic = "hr/setup",
        },
        new()
        {
            Key = $"{Id}.Payroll.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Payroll run numbers", "ترقيم مسيّرات الرواتب"),
            Description = new LocalizedText(
                "The series payroll runs are numbered from.",
                "المسلسل الذي تصدر منه أرقام مسيّرات الرواتب."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "PAYROLL",
            RequiresPermission = $"{Id}.Wage.Update",
            HelpTopic = "hr/setup",
        },
        new()
        {
            Key = $"{Id}.Posting.LeaveProvisionAccount",
            Module = Id,
            Group = new LocalizedText("Posting", "الترحيل"),
            DisplayName = new LocalizedText("Unused leave provision", "مخصص الإجازات غير المستخدمة"),
            Description = new LocalizedText(
                "Where unused leave is carried. Days somebody has earned and not taken are owed "
                + "to them in money on the day they leave.",
                "الحساب الذي تُحمَّل عليه الإجازات غير المستخدمة. فالأيام المستحقة وغير المستخدمة "
                + "تُصرف نقدًا عند ترك العمل."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "2400",
            RequiresPermission = $"{Id}.Wage.Update",
            HelpTopic = "hr/setup",
        },
        new()
        {
            Key = $"{Id}.Leave.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Leave request numbers", "ترقيم طلبات الإجازة"),
            Description = new LocalizedText(
                "The series leave requests are numbered from.",
                "المسلسل الذي تصدر منه أرقام طلبات الإجازة."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "LEAVE",
            RequiresPermission = $"{Id}.Employee.Update",
            HelpTopic = "hr/setup",
        },
        new()
        {
            Key = $"{Id}.Employees.NumberSeries",
            Module = Id,
            Group = new LocalizedText("Numbering", "الترقيم"),
            DisplayName = new LocalizedText("Employee numbers", "ترقيم الموظفين"),
            Description = new LocalizedText(
                "The series employee numbers are issued from. Never reuse a leaver's number: it "
                + "would attach one person's service history to another, and service history is "
                + "what an end-of-service award is calculated from.",
                "المسلسل الذي تصدر منه أرقام الموظفين. ولا تُعاد أرقام من ترك العمل، فذلك يربط سجل "
                + "خدمة شخص بآخر، وسجل الخدمة هو أساس احتساب مكافأة نهاية الخدمة."),
            ValueType = SetupValueType.Text,
            Scope = SetupScope.Company,
            DefaultValue = "EMP",
            RequiresPermission = $"{Id}.Employee.Update",
            HelpTopic = "hr/setup",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = "Hr.Root",
            Module = Id,
            DisplayName = new LocalizedText("Human resources", "الموارد البشرية"),
            Kind = NavigationKind.Group,
            Icon = "hr",
            Order = 600,
        },
        new()
        {
            Id = "Hr.Employees",
            Module = Id,
            ParentId = "Hr.Root",
            DisplayName = new LocalizedText("Employees", "الموظفون"),
            Kind = NavigationKind.Page,
            Route = "/hr/employees",
            RequiresPermission = $"{Id}.Employee.Read",
            Order = 10,
        },
        new()
        {
            Id = "Hr.Payroll",
            Module = Id,
            ParentId = "Hr.Root",
            DisplayName = new LocalizedText("Payroll", "الرواتب"),
            Kind = NavigationKind.Page,
            Route = "/hr/payroll",
            RequiresPermission = $"{Id}.Wage.Read",
            Order = 15,
        },
        new()
        {
            Id = "Hr.Leave",
            Module = Id,
            ParentId = "Hr.Root",
            DisplayName = new LocalizedText("Leave", "الإجازات"),
            Kind = NavigationKind.Page,
            Route = "/hr/leave",
            RequiresPermission = $"{Id}.Leave.Read",
            Order = 12,
        },
        new()
        {
            Id = "Hr.Entitlements",
            Module = Id,
            ParentId = "Hr.Root",
            DisplayName = new LocalizedText("What the company owes", "التزامات الشركة"),
            Kind = NavigationKind.Report,
            Route = "/hr/entitlements",
            RequiresPermission = $"{Id}.Report.Read",
            Order = 20,
        },
    ];
}
