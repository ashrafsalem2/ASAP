using Acme.Warranty;
using ASAP.Extensions.Sdk;
using ASAP.Platform.Kernel.Time;
using Shouldly;

namespace Acme.Warranty.Tests;

/// <summary>
/// The tests an extension author would actually write.
/// </summary>
/// <remarks>
/// Two kinds, and the first is the one worth copying: a single call that holds this extension to
/// the same rules ASAP holds its own modules to. Without it an author finds out from a customer
/// that a refusal they wrote says something is impossible and not what to do instead.
/// </remarks>
public sealed class WarrantyTests
{
    [Fact]
    public void The_extension_conforms()
    {
        // One line, and it checks every declaration: a blocking message with no resolution, a
        // message missing its Arabic or dropping a placeholder in translation, a permission
        // implying one nothing declares, a menu entry needing a permission that is not there.
        ExtensionCheck.ThrowIfNotConforming(new WarrantyExtension());
    }

    [Fact]
    public void Everything_it_declares_belongs_to_it()
    {
        var extension = new WarrantyExtension();

        // The prefix is what stops two extensions quietly sharing a setting or a permission, so
        // it is worth asserting rather than assuming.
        extension.Permissions.ShouldAllBe(p => p.Key.StartsWith("Acme.Warranty."));
        extension.Setups.ShouldAllBe(s => s.Key.StartsWith("Acme.Warranty."));
        extension.Messages.ShouldAllBe(m => m.Code.Value.StartsWith("ACME.WARRANTY."));
    }

    [Fact]
    public void A_warranty_runs_in_months_rather_than_days()
    {
        var calculator = new WarrantyCalculator(new StubClock(new DateOnly(2026, 3, 1)));

        // Twelve months from 31 August is 31 August, not a date three hundred and sixty-five days
        // later that lands on the thirtieth in a leap year and produces an argument at a counter.
        var status = calculator.Check("S-1", new DateOnly(2025, 8, 31), 12);

        status.ExpiresOn.ShouldBe(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void A_month_that_is_too_short_clamps_rather_than_spilling_over()
    {
        var calculator = new WarrantyCalculator(new StubClock(new DateOnly(2026, 2, 1)));

        // Sold on 31 January with one month: covered to 28 February, which is what a customer
        // expects and what a court would say. Thirty days would give 2 March.
        calculator.Check("S-2", new DateOnly(2026, 1, 31), 1)
                  .ExpiresOn.ShouldBe(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void The_last_day_is_covered()
    {
        var expiry = new DateOnly(2026, 8, 31);
        var calculator = new WarrantyCalculator(new StubClock(expiry));

        // A customer arriving on the day it expires is covered, because that is what everybody
        // outside a software company understands "twelve months" to mean.
        var status = calculator.Check("S-3", new DateOnly(2025, 8, 31), 12);

        status.IsCovered.ShouldBeTrue();
        status.DaysLeft.ShouldBe(0);
    }

    [Fact]
    public void How_long_ago_it_expired_is_reported_rather_than_clamped()
    {
        var calculator = new WarrantyCalculator(new StubClock(new DateOnly(2026, 9, 3)));

        var status = calculator.Check("S-4", new DateOnly(2025, 8, 31), 12);

        status.IsCovered.ShouldBeFalse();

        // "Expired three days ago" is a conversation a counter assistant can have. "Expired" is
        // not, and the customer standing there knows the difference.
        status.DaysLeft.ShouldBe(-3);
    }

    [Fact]
    public void A_warranty_of_no_months_expires_the_day_it_was_sold()
    {
        var calculator = new WarrantyCalculator(new StubClock(new DateOnly(2026, 3, 2)));

        var status = calculator.Check("S-5", new DateOnly(2026, 3, 1), 0);

        status.ExpiresOn.ShouldBe(new DateOnly(2026, 3, 1));
        status.IsCovered.ShouldBeFalse();
    }

    private sealed class StubClock(DateOnly today) : IClock
    {
        public DateTime UtcNow { get; } = today.ToDateTime(TimeOnly.MinValue);

        public DateOnly Today { get; } = today;
    }
}
