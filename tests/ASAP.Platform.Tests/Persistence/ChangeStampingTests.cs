using ASAP.Platform.Core.Numbering;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Platform.Tests.Persistence;

/// <summary>
/// Covers what the context fills in on save: tenancy, audit stamps, and turning a delete into a
/// soft delete.
/// </summary>
public sealed class ChangeStampingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Branch = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Alice = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Bob = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");

    private readonly TestContextHarness _harness = new();

    public ChangeStampingTests()
    {
        _harness.Tenancy.TenantId = Tenant;
        _harness.Tenancy.CompanyId = Company;
        _harness.Tenancy.BranchId = Branch;
        _harness.User.UserId = Alice;
        _harness.User.UserName = "alice";
    }

    private static NumberSeries NewSeries(string code = "SALES-INV")
        => new() { Code = code, Description = "Sales invoices" };

    [Fact]
    public void Stamps_the_active_tenant_and_company_on_a_new_row()
    {
        // The caller never sets these. A module author writing context.Add(thing) gets correct
        // tenancy because the context supplies it, not because they remembered to.
        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Add(NewSeries());
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            var series = context.NumberSeries.Single();
            series.TenantId.ShouldBe(Tenant);
            series.CompanyId.ShouldBe(Company);
        }
    }

    [Fact]
    public void Does_not_overwrite_tenancy_that_was_set_deliberately()
    {
        // The seeder and cross-company routines set these explicitly, and must not have their
        // work replaced by whatever the ambient context happens to hold.
        var otherCompany = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

        _harness.AsSystem(context =>
        {
            var series = NewSeries();
            series.TenantId = Tenant;
            series.CompanyId = otherCompany;
            context.NumberSeries.Add(series);
            context.SaveChanges();
        });

        _harness.AsSystem(context =>
            context.NumberSeries.Single().CompanyId.ShouldBe(otherCompany));
    }

    [Fact]
    public void Records_who_created_a_row_and_when()
    {
        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Add(NewSeries());
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            var series = context.NumberSeries.Single();
            series.CreatedAtUtc.ShouldBe(_harness.Clock.UtcNow);
            series.CreatedBy.ShouldBe(Alice);
            series.ModifiedAtUtc.ShouldBeNull();
            series.ModifiedBy.ShouldBeNull();
        }
    }

    [Fact]
    public void Records_who_changed_a_row_without_disturbing_who_created_it()
    {
        var created = _harness.Clock.UtcNow;

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Add(NewSeries());
            context.SaveChanges();
        }

        // A different person, an hour later.
        _harness.Clock.UtcNow = created.AddHours(1);
        _harness.User.UserId = Bob;

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Single().Description = "Renamed";
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            var series = context.NumberSeries.Single();
            series.CreatedAtUtc.ShouldBe(created);
            series.CreatedBy.ShouldBe(Alice);
            series.ModifiedAtUtc.ShouldBe(created.AddHours(1));
            series.ModifiedBy.ShouldBe(Bob);
        }
    }

    [Fact]
    public void Records_who_deleted_a_row()
    {
        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Add(NewSeries());
            context.SaveChanges();
        }

        _harness.User.UserId = Bob;

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Remove(context.NumberSeries.Single());
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            var deleted = context.NumberSeries.IgnoreQueryFilters().Single();
            deleted.IsDeleted.ShouldBeTrue();
            deleted.DeletedBy.ShouldBe(Bob);
            deleted.DeletedAtUtc.ShouldBe(_harness.Clock.UtcNow);
        }
    }

    [Fact]
    public void A_delete_leaves_the_row_in_place()
    {
        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Add(NewSeries());
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.Remove(context.NumberSeries.Single());
            context.SaveChanges();
        }

        using (var context = _harness.NewContext())
        {
            context.NumberSeries.IgnoreQueryFilters().Count().ShouldBe(1);
        }
    }

    public void Dispose() => _harness.Dispose();
}
