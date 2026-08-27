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
