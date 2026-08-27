using ASAP.Modules.Finance.Periods;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Periods;

/// <summary>
/// Covers the transfer that empties the income statement into retained earnings.
/// </summary>
/// <remarks>
/// The whole of a year end is guard rails except for this. Get the sign wrong and a profitable
/// year reduces what the owners have; miss the per-branch split and every shop's revenue account
/// keeps a balance the company total says is zero.
/// </remarks>
public sealed class YearEndLinesTests
{
    private static readonly Guid Jeddah = Guid.Parse("cccccccc-0000-0000-0000-00000000001a");
    private static readonly Guid Riyadh = Guid.Parse("cccccccc-0000-0000-0000-00000000002a");
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    [Fact]
    public void A_profit_is_credited_to_retained_earnings()
    {
        // Revenue of 1,000 (a credit, so negative) against expenses of 400. The result is 600 and
        // it belongs to the owners, which on an equity account is a credit.
        var (lines, result) = Build(
            new YearEndBalance("4100", null, -1_000m),
            new YearEndBalance("6100", null, 400m));

        result.ShouldBe(600m);

        var retained = lines.Single(l => l.AccountNo == "3200");

        retained.Amount.ShouldBe(-600m);
    }

    [Fact]
    public void A_loss_is_debited_to_retained_earnings()
    {
        var (lines, result) = Build(
            new YearEndBalance("4100", null, -1_000m),
            new YearEndBalance("6100", null, 1_750m));

        result.ShouldBe(-750m);
        lines.Single(l => l.AccountNo == "3200").Amount.ShouldBe(750m);
    }

    [Fact]
    public void Every_account_is_left_at_nothing()
    {
        var balances = new[]
        {
            new YearEndBalance("4100", null, -1_000m),
            new YearEndBalance("5100", null, 300m),
            new YearEndBalance("6100", null, 400m),
        };

        var (lines, _) = Build(balances);

        foreach (var balance in balances)
        {
            var cleared = balance.Amount + lines
                .Where(l => l.AccountNo == balance.AccountNo)
                .Sum(l => l.Amount);

            cleared.ShouldBe(0m);
        }
    }

    [Fact]
    public void The_transfer_balances()
    {
        // It reaches the ledger through the posting engine, which refuses anything that does not
        // sum to zero. Better to know here than to find out at the year end.
        var (lines, _) = Build(
            new YearEndBalance("4100", Jeddah, -1_200m),
            new YearEndBalance("4100", Riyadh, -800m),
            new YearEndBalance("6100", Jeddah, 500m),
            new YearEndBalance("6100", Riyadh, 900m));

        lines.Sum(static l => l.Amount).ShouldBe(0m);
    }

    [Fact]
    public void Each_branch_is_cleared_where_it_was_carried()
    {
        var (lines, _) = Build(
            new YearEndBalance("4100", Jeddah, -1_200m),
            new YearEndBalance("4100", Riyadh, -800m));

        lines.Single(l => l.AccountNo == "4100" && l.BranchId == Jeddah).Amount.ShouldBe(1_200m);
        lines.Single(l => l.AccountNo == "4100" && l.BranchId == Riyadh).Amount.ShouldBe(800m);
    }

    [Fact]
    public void The_result_goes_whole_and_names_no_branch()
    {
        var (lines, result) = Build(
            new YearEndBalance("4100", Jeddah, -1_200m),
            new YearEndBalance("4100", Riyadh, -800m));

        var retained = lines.Where(l => l.AccountNo == "3200").ToList();

        retained.Count.ShouldBe(1);
        retained[0].BranchId.ShouldBeNull();
        retained[0].Amount.ShouldBe(-result);
    }

    [Fact]
    public void An_account_that_did_not_move_gets_no_entry()
    {
        // Otherwise every account in the chart gets a row on the ledger every year, saying that
        // nothing happened to it.
        var (lines, _) = Build(
            new YearEndBalance("4100", null, -1_000m),
            new YearEndBalance("4200", null, 0m));

        lines.ShouldNotContain(l => l.AccountNo == "4200");
    }

    [Fact]
    public void A_year_with_no_trading_posts_nothing_at_all()
    {
        var (lines, result) = Build(new YearEndBalance("4100", null, 0m));

        lines.ShouldBeEmpty();
        result.ShouldBe(0m);
    }

    [Fact]
    public void Every_line_is_dated_the_last_day_of_the_year()
    {
        // A transfer posted on the day somebody happened to run it would put last year's result
        // in this year, which is the one thing the whole routine exists to prevent.
        var (lines, _) = Build(new YearEndBalance("4100", null, -1_000m));

        lines.ShouldAllBe(l => l.PostingDate == YearEnd);
    }

    private static (IReadOnlyList<ASAP.Modules.Finance.Journals.PostJournalLine> Lines, decimal Result) Build(
        params YearEndBalance[] balances)
        => YearEndLines.For(balances, "3200", "Year end 2026", YearEnd);
}
