using ASAP.Extensions.Sdk;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Warranty;

/// <summary>
/// A worked example: warranty tracking, built the way a real extension would be.
/// </summary>
/// <remarks>
/// <para>
/// It answers one question — is this sale still under warranty — and everything else here exists
/// because answering it properly requires it: somebody has to be allowed to ask, the length has
/// to be configurable, and the answer "no" has to say why and what to do about it.
/// </para>
/// <para>
/// The point of the sample is that none of this uses a mechanism reserved for extensions. Every
/// declaration below is the same one Finance and Inventory make, which is what makes the claim
/// "anything they can do, yours can do too" checkable rather than reassuring.
/// </para>
/// </remarks>
public sealed class WarrantyExtension : AsapExtension
{
    /// <inheritdoc />
    public override string ModuleId => "Acme.Warranty";

    /// <inheritdoc />
    public override LocalizedText DisplayName => new("Warranty tracking", "تتبع الضمان");

    /// <inheritdoc />
    public override LocalizedText Description => new(
        "How long the things this shop sells stay under warranty, and whether a particular sale "
        + "still is.",
        "المدة التي تبقى فيها مبيعات هذا المتجر تحت الضمان، وهل لا تزال عملية بيع بعينها كذلك.");

    /// <inheritdoc />
    public override string Publisher => "Acme Software";

    /// <summary>
    /// Sales, because a warranty is measured from the day something was sold.
    /// </summary>
    /// <remarks>
    /// Naming it is a promise the platform keeps: an installation without Sales refuses this
    /// extension at startup rather than letting it fail later on the one screen that needed it.
    /// </remarks>
    public override IReadOnlyCollection<string> DependsOn => ["Sales"];

    /// <inheritdoc />
    public override IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            ModuleId, "Warranty", PermissionAction.Read,
            new LocalizedText("Check a warranty", "الاستعلام عن ضمان"),
            new LocalizedText(
                "Ask whether a sale is still under warranty. A counter assistant needs this; it "
                + "says nothing about what the sale was worth.",
                "الاستعلام عمّا إذا كانت عملية بيع لا تزال تحت الضمان. يحتاجها موظف الكاونتر، وهي "
                + "لا تكشف شيئًا عن قيمة البيع.")),

        PermissionDescriptor.Define(
            ModuleId, "Warranty", PermissionAction.Update,
            new LocalizedText("Change the warranty period", "تغيير مدة الضمان"),
            new LocalizedText(
                "Change how long things stay under warranty. It applies to sales already made, so "
                + "shortening it takes cover away from customers who already have it.",
                "تغيير المدة التي تبقى فيها المبيعات تحت الضمان. وهي تسري على مبيعات تمت بالفعل، "
                + "فتقصيرها يسحب التغطية من عملاء يملكونها الآن."),
            implies: [Permission("Warranty", PermissionAction.Read)],
            isSensitive: true),
    ];

    /// <inheritdoc />
    public override IReadOnlyCollection<SetupDescriptor> Setups =>
    [
        new()
        {
            Key = Setting("Months"),
            Module = ModuleId,
            Group = new LocalizedText("Warranty", "الضمان"),
            DisplayName = new LocalizedText("Warranty period in months", "مدة الضمان بالأشهر"),
            Description = new LocalizedText(
                "How long after the sale something stays under warranty. Measured from the day it "
                + "was sold rather than the day it was delivered, because the receipt is what the "
                + "customer keeps and the delivery note is not.",
                "المدة التي يبقى فيها المنتج تحت الضمان بعد بيعه. وتُحسب من يوم البيع لا يوم "
                + "التسليم، لأن الإيصال هو ما يحتفظ به العميل لا مذكرة التسليم."),
            ValueType = SetupValueType.Integer,
            Scope = SetupScope.Company,
            DefaultValue = "12",
            Minimum = 0,
            Maximum = 120,
            RequiresPermission = Permission("Warranty", PermissionAction.Update),
        },
    ];

    /// <inheritdoc />
    public override IReadOnlyCollection<MessageDefinition> Messages =>
    [
        new()
        {
            Code = Expired,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("The warranty has run out", "انتهى الضمان"),
            Detail = new LocalizedText(
                "{DocumentNo} was sold on {SoldOn:d} and the warranty ran to {ExpiredOn:d}, "
                + "which was {DaysAgo} day(s) ago.",
                "بيع {DocumentNo} في {SoldOn:d} وامتد الضمان إلى {ExpiredOn:d}، أي قبل "
                + "{DaysAgo} يومًا."),

            // A refusal that blocks somebody has to say what to do next. This is the rule the SDK
            // check enforces, and the reason a system's refusals are usable rather than final.
            Resolution = new LocalizedText(
                "Charge for the repair, or check whether the customer holds a separate extended "
                + "warranty. If the sale date is wrong on the receipt, correct that first — this "
                + "is measured from it.",
                "احتسب قيمة الإصلاح، أو تحقق مما إذا كان لدى العميل ضمان ممتد منفصل. وإن كان "
                + "تاريخ البيع خاطئًا في الإيصال فصححه أولًا، فالمدة تُحسب منه."),
            HelpTopic = "sales/orders",
        },
        new()
        {
            Code = NotSold,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No sale by that number", "لا توجد عملية بيع بهذا الرقم"),
            Detail = new LocalizedText(
                "Nothing in this company was sold under {DocumentNo}.",
                "لا يوجد في هذه الشركة ما بيع تحت الرقم {DocumentNo}."),
            Resolution = new LocalizedText(
                "Check the number on the receipt. A sale made in another company will not be "
                + "found here.",
                "تحقق من الرقم في الإيصال. فالبيع الذي تم في شركة أخرى لن يُعثر عليه هنا."),
            HelpTopic = "sales/orders",
        },
    ];

    /// <inheritdoc />
    public override IReadOnlyCollection<NavigationItem> Navigation =>
    [
        new()
        {
            Id = $"{ModuleId}.Check",
            Module = ModuleId,
            ParentId = "Sales.Root",
            DisplayName = new LocalizedText("Warranty check", "فحص الضمان"),
            Kind = NavigationKind.Task,
            Route = "/sales/warranty",
            RequiresPermission = Permission("Warranty", PermissionAction.Read),
            Order = 90,
        },
    ];

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<WarrantyCalculator>();
    }

    /// <summary>Asked about a sale whose warranty has run out.</summary>
    public MessageCode Expired => Code("Warranty", "Expired");

    /// <summary>Asked about a sale nobody made.</summary>
    public MessageCode NotSold => Code("Warranty", "NotSold");
}
