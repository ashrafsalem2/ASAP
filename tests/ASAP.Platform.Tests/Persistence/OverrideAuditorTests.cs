using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Platform.Tests.Persistence;

/// <summary>
/// Covers the promise the platform makes on every overridable refusal: that pushing past it is
/// recorded, once, against the name of whoever did it.
/// </summary>
/// <remarks>
/// The text a user sees when a block is overridden says the override has been recorded. These
/// tests are what makes that sentence true rather than merely printed.
/// </remarks>
public sealed class OverrideAuditorTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000021");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000002a");
    private static readonly Guid Salim = Guid.Parse("eeeeeeee-0000-0000-0000-00000000002e");

    private readonly TestContextHarness _harness = new();

    public OverrideAuditorTests()
    {
        _harness.Tenancy.TenantId = Tenant;
        _harness.Tenancy.CompanyId = Company;
        _harness.User.UserId = Salim;
        _harness.User.UserName = "salim";
    }

    private static AsapMessage Overridden(string code = "INV.STOCK.NEGATIVE_BLOCKED")
        => new()
        {
            Code = new MessageCode(code),
            Severity = MessageSeverity.Warning,
            Title = "Selling stock that is not there",
            Detail = "JED-01 holds none of ITEM-1001 and this ships 2.",
            OverridePermission = "Inventory.Stock.Override",
            WasOverridden = true,
        };

    private static AsapMessage PlainWarning()
        => new()
        {
            Code = new MessageCode("INV.ITEM.BELOW_REORDER_POINT"),
            Severity = MessageSeverity.Warning,
            Title = "Below its reorder point",
            OverridePermission = "Inventory.Stock.Override",
            WasOverridden = false,
        };

    [Fact]
    public void Records_the_block_that_was_pushed_past()
    {
        using var context = _harness.NewContext();
        var auditor = NewAuditor(context);

        auditor.Record([Overridden()], "Inventory.ItemLedgerEntry", "SO-0003", "customer waiting")
            .ShouldBe(1);

        context.SaveChanges();

        var row = context.AuditLog.AsNoTracking().Single();
        row.Action.ShouldBe(AuditAction.Override);
        row.OverriddenMessageCode.ShouldBe("INV.STOCK.NEGATIVE_BLOCKED");
        row.DisplayNo.ShouldBe("SO-0003");
        row.OverrideReason.ShouldBe("customer waiting");
        row.UserName.ShouldBe("salim");

        // The figures behind the refusal, which is what makes the row worth reading a year later.
        row.Changes.ShouldBe("JED-01 holds none of ITEM-1001 and this ships 2.");
    }

    [Fact]
    public void Leaves_a_warning_that_was_never_a_refusal_alone()
    {
        // Severity plus an override permission is also the shape of an ordinary warning. Logging
        // those as overrides would bury the real ones under the noise.
        using var context = _harness.NewContext();

        NewAuditor(context).Record([PlainWarning()], "Inventory.ItemLedgerEntry", "SO-0003")
            .ShouldBe(0);

        context.SaveChanges();
        context.AuditLog.AsNoTracking().ShouldBeEmpty();
    }

    [Fact]
    public void Records_one_decision_once_however_many_layers_see_it()
    {
        // Sales asks Inventory to move stock, then passes the messages it got back to its own
        // Record call. Both calls are right to make; the trail must still show one override,
        // because "how often did we sell below cost" is a question people answer by counting.
        using var context = _harness.NewContext();
        var auditor = NewAuditor(context);
        var message = Overridden();

        auditor.Record([message], "Inventory.ItemLedgerEntry", "SO-0003", "customer waiting")
            .ShouldBe(1);

        auditor.Record([message], "Sales.Shipment", "SO-0003", "customer waiting")
            .ShouldBe(0, "the layer below already wrote it");

        context.SaveChanges();

        var row = context.AuditLog.AsNoTracking().Single();
        row.EntityType.ShouldBe(
            "Inventory.ItemLedgerEntry",
            "the row should name the layer that actually raised the block");
    }

    [Fact]
    public void Two_separate_refusals_of_the_same_kind_are_two_overrides()
    {
        // A shipment with two lines short is two decisions, not one seen twice. Deduplicating by
        // value rather than identity would quietly halve the count.
        using var context = _harness.NewContext();

        NewAuditor(context)
            .Record([Overridden(), Overridden()], "Inventory.ItemLedgerEntry", "SO-0003")
            .ShouldBe(2);

        context.SaveChanges();
        context.AuditLog.AsNoTracking().Count().ShouldBe(2);
    }

    private OverrideAuditor NewAuditor(AsapDbContext context)
        => new(context, _harness.Tenancy, _harness.User, _harness.Clock);

    public void Dispose() => _harness.Dispose();
}
