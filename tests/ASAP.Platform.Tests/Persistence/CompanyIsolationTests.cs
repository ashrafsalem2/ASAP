using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Platform.Tests.Persistence;

/// <summary>
/// Proves the company boundary actually holds. These are the most important tests in the
/// platform: if a query filter fails, one customer sees another books, and no amount of correct
/// business logic above it matters.
/// </summary>
public sealed class CompanyIsolationTests : IDisposable
{
    private static readonly Guid TenantOne = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid TenantTwo = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid TradingCompany = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid PropertyCompany = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid OtherTenantCompany = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    private readonly TestContextHarness _harness = new();

    public CompanyIsolationTests()
    {
        // Seed as the system, the way the installer does, so rows land in companies the ambient
        // context is not currently pointing at.
        _harness.AsSystem(context =>
        {
            context.NumberSeries.AddRange(
                Series(TenantOne, TradingCompany, "SALES-INV", "Trading sales invoices"),
                Series(TenantOne, TradingCompany, "PURCH-INV", "Trading purchase invoices"),
                Series(TenantOne, PropertyCompany, "SALES-INV", "Property sales invoices"),
                Series(TenantTwo, OtherTenantCompany, "SALES-INV", "Another tenant invoices"));

            context.SaveChanges();
        });
    }

    private static NumberSeries Series(Guid tenantId, Guid companyId, string code, string description)
        => new()
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Code = code,
            Description = description,
        };

    private void ActAs(Guid tenantId, Guid companyId)
    {
        _harness.Tenancy.TenantId = tenantId;
        _harness.Tenancy.CompanyId = companyId;
        _harness.Tenancy.IsCrossTenantOperation = false;
    }

    [Fact]
    public void A_company_sees_only_its_own_rows()
    {
        ActAs(TenantOne, TradingCompany);

        using var context = _harness.NewContext();

        var codes = context.NumberSeries.Select(n => n.Description).ToList();

        codes.ShouldBe(["Trading sales invoices", "Trading purchase invoices"], ignoreOrder: true);
    }

    [Fact]
    public void A_sibling_company_in_the_same_tenant_is_invisible()
    {
        ActAs(TenantOne, PropertyCompany);

        using var context = _harness.NewContext();

        context.NumberSeries.Select(n => n.Description).ToList()
               .ShouldBe(["Property sales invoices"]);
    }

    [Fact]
    public void Another_tenant_data_is_invisible()
    {
        ActAs(TenantOne, TradingCompany);

        using var context = _harness.NewContext();

        context.NumberSeries.Any(n => n.TenantId == TenantTwo).ShouldBeFalse();
    }

    [Fact]
    public void The_filter_follows_the_caller_rather_than_the_first_context_ever_built()
    {
        // The trap this test exists for. EF builds the model once and caches it for the lifetime
        // of the process. If the filters had captured a tenant value at model-building time
        // instead of reading it off the context per query, every later request would silently be
        // served the first caller data -- and it would look perfectly correct in any test that
        // only ever used one company.
        ActAs(TenantOne, TradingCompany);
        using (var first = _harness.NewContext())
        {
            first.NumberSeries.Count().ShouldBe(2);
        }

        ActAs(TenantOne, PropertyCompany);
        using (var second = _harness.NewContext())
        {
            second.NumberSeries.Count().ShouldBe(1);
            second.NumberSeries.Single().Description.ShouldBe("Property sales invoices");
        }

        ActAs(TenantTwo, OtherTenantCompany);
        using (var third = _harness.NewContext())
        {
            third.NumberSeries.Single().Description.ShouldBe("Another tenant invoices");
        }
    }

    [Fact]
    public void An_unauthenticated_request_sees_nothing()
    {
        // The safe default. With no tenant on the request the filter compares against null and
        // matches no row, rather than falling open and returning everything.
        _harness.Tenancy.TenantId = null;
        _harness.Tenancy.CompanyId = null;
        _harness.Tenancy.IsCrossTenantOperation = false;

        using var context = _harness.NewContext();

        context.NumberSeries.ShouldBeEmpty();
    }

    [Fact]
    public void A_system_operation_sees_every_tenant()
    {
        _harness.AsSystem(context => context.NumberSeries.Count().ShouldBe(4));
    }

    [Fact]
    public void A_soft_deleted_row_disappears_from_queries()
    {
        ActAs(TenantOne, TradingCompany);

        using (var context = _harness.NewContext())
        {
            var series = context.NumberSeries.Single(n => n.Code == "PURCH-INV");
            context.NumberSeries.Remove(series);
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Select(n => n.Code).ToList().ShouldBe(["SALES-INV"]);
        }
    }

    [Fact]
    public void A_soft_deleted_row_is_still_there_underneath()
    {
        // Master data is hidden, not removed, because posted history keeps pointing at it.
        ActAs(TenantOne, TradingCompany);

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Remove(context.NumberSeries.Single(n => n.Code == "PURCH-INV"));
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            var deleted = context.NumberSeries
                                 .IgnoreQueryFilters()
                                 .Single(n => n.Code == "PURCH-INV" && n.CompanyId == TradingCompany);

            deleted.IsDeleted.ShouldBeTrue();
            deleted.DeletedAtUtc.ShouldBe(_harness.Clock.UtcNow);
        }
    }

    [Fact]
    public void Ignoring_the_filters_still_requires_asking_for_it_explicitly()
    {
        // IgnoreQueryFilters is the deliberate escape hatch for consolidation reporting. It has
        // to be written out at the call site, which is what makes it reviewable.
        ActAs(TenantOne, TradingCompany);

        using var context = _harness.NewContext();

        context.NumberSeries.IgnoreQueryFilters().Count().ShouldBe(4);
    }

    public void Dispose() => _harness.Dispose();
}
