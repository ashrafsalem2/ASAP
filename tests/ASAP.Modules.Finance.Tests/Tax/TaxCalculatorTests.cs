using ASAP.Modules.Finance.Tax;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Tax;

/// <summary>
/// Covers the arithmetic a tax return stands on.
/// </summary>
/// <remarks>
/// Being a halala out here is not a rounding detail; it is a return that does not reconcile, found
/// at the end of a quarter by somebody who then has to explain it.
/// </remarks>
public sealed class TaxCalculatorTests
{
    [Fact]
    public void Tax_is_added_to_a_net_amount()
    {
        var amounts = TaxCalculator.FromNet(100m, 15m);

        amounts.Base.ShouldBe(100m);
        amounts.Tax.ShouldBe(15m);
        amounts.Total.ShouldBe(115m);
    }

    [Fact]
    public void Tax_inside_a_gross_amount_is_the_fraction_not_the_percentage()
    {
        // The mistake that overstates the tax on every sale a till makes: 15% of 115 is 17.25,
        // but the tax inside 115 is 15.00. The fraction is rate / (100 + rate), which at 15% is
        // 3/23 rather than 3/20.
        var amounts = TaxCalculator.FromGross(115m, 15m);

        amounts.Tax.ShouldBe(15m);
        amounts.Base.ShouldBe(100m);
        amounts.Total.ShouldBe(115m);
    }

    [Fact]
    public void A_gross_amount_always_adds_back_to_itself()
    {
        // The base is what is left after the tax, not a separately rounded figure. Rounding both
        // and hoping is how a line's own numbers come to disagree with its total.
        foreach (var gross in new[] { 0.01m, 1.99m, 33.33m, 99.99m, 1_234.56m, 7_777.77m })
        {
            var amounts = TaxCalculator.FromGross(gross, 15m);

            (amounts.Base + amounts.Tax).ShouldBe(gross, $"gross of {gross} must not lose a halala");
            amounts.Total.ShouldBe(gross);
        }
    }

    [Fact]
    public void Zero_rated_charges_nothing_but_still_has_a_base()
    {
        // Which is the whole reason zero-rated is not the same as no tax code at all: the supply
        // is still declared, and its base is still a box on the return.
        var amounts = TaxCalculator.FromNet(500m, 0m);

        amounts.Base.ShouldBe(500m);
        amounts.Tax.ShouldBe(0m);
        amounts.Total.ShouldBe(500m);
    }

    [Fact]
    public void A_document_is_taxed_on_its_total_rather_than_line_by_line()
    {
        // Twenty lines of 0.33 at 15%: each line alone rounds to 0.05, totalling 1.00, while the
        // document total of 6.60 carries 0.99. The customer's arithmetic is the one that has to
        // be right, so the document total wins.
        var lines = Enumerable.Repeat(0.33m, 20).ToList();

        var perLine = lines.Sum(line => TaxCalculator.FromNet(line, 15m).Tax);
        var document = TaxCalculator.ForDocument(lines, 15m);

        perLine.ShouldBe(1.00m, "this is the wrong answer, and it is what line-by-line produces");
        document.Base.ShouldBe(6.60m);
        document.Tax.ShouldBe(0.99m);
        document.Total.ShouldBe(7.59m);
    }

    [Fact]
    public void A_tax_inclusive_document_works_backwards_from_its_total()
    {
        var lines = new[] { 57.50m, 57.50m };

        var document = TaxCalculator.ForDocument(lines, 15m, taxIncluded: true);

        document.Total.ShouldBe(115m);
        document.Tax.ShouldBe(15m);
        document.Base.ShouldBe(100m);
    }

    [Fact]
    public void Allocating_tax_across_lines_never_loses_or_invents_a_halala()
    {
        // Three equal lines of a tax that does not divide by three. Somebody has to take the odd
        // halala, and the point is that exactly one line does.
        var lines = new[] { 10m, 10m, 10m };
        var shares = TaxCalculator.Allocate(lines, 1.00m);

        shares.Sum().ShouldBe(1.00m);
        shares.Count.ShouldBe(3);
        shares[0].ShouldBe(0.33m);
        shares[1].ShouldBe(0.33m);
        shares[2].ShouldBe(0.34m, "the last line carries the remainder");
    }

    [Fact]
    public void Allocation_follows_the_size_of_each_line()
    {
        var lines = new[] { 90m, 10m };
        var shares = TaxCalculator.Allocate(lines, 15m);

        shares[0].ShouldBe(13.50m);
        shares[1].ShouldBe(1.50m);
        shares.Sum().ShouldBe(15m);
    }

    [Fact]
    public void Allocation_holds_up_on_awkward_numbers()
    {
        var lines = new[] { 1m, 1m, 1m, 1m, 1m, 1m, 1m };

        foreach (var total in new[] { 0.01m, 0.07m, 1.23m, 99.99m, -4.44m })
        {
            var shares = TaxCalculator.Allocate(lines, total);

            shares.Sum().ShouldBe(total, $"a tax total of {total} must survive being split seven ways");
        }
    }

    [Fact]
    public void Tax_on_lines_that_come_to_nothing_is_still_kept()
    {
        // A document whose lines net to zero can still carry tax -- a credit and a charge of the
        // same size at different rates. Dropping it because the basis is zero would lose tax the
        // document says it carries.
        var shares = TaxCalculator.Allocate([5m, -5m], 0.75m);

        shares.Sum().ShouldBe(0.75m);
    }

    [Fact]
    public void A_credit_note_carries_negative_tax()
    {
        var amounts = TaxCalculator.FromNet(-200m, 15m);

        amounts.Tax.ShouldBe(-30m);
        amounts.Total.ShouldBe(-230m);

        // Rounding is symmetric about zero, which is what matters here: a credit note must carry
        // exactly the tax of the invoice it reverses, with the sign turned round. Anything that
        // rounds the two differently leaves a residue on the tax account that nothing explains.
        foreach (var amount in new[] { 0.01m, 0.04m, 0.37m, 3.33m, 12.345m, 999.99m })
        {
            TaxCalculator.FromNet(-amount, 15m).Tax
                .ShouldBe(-TaxCalculator.FromNet(amount, 15m).Tax, $"reversing {amount}");

            TaxCalculator.FromGross(-amount, 15m).Tax
                .ShouldBe(-TaxCalculator.FromGross(amount, 15m).Tax, $"reversing gross {amount}");
        }
    }

    [Fact]
    public void A_rate_is_read_from_the_date_the_document_carries()
    {
        // Saudi Arabia went from 5% to 15% in July 2020. A credit note against a 2019 invoice has
        // to carry 2019's rate, or it will not offset the invoice it corrects.
        var code = new TaxCode
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            Code = "VAT",
            Description = "Value added tax",
        };

        code.Rates.Add(new TaxRate { StartingDate = new DateOnly(2018, 1, 1), Percentage = 5m });
        code.Rates.Add(new TaxRate { StartingDate = new DateOnly(2020, 7, 1), Percentage = 15m });

        code.RateOn(new DateOnly(2019, 6, 1)).ShouldBe(5m);
        code.RateOn(new DateOnly(2020, 6, 30)).ShouldBe(5m);
        code.RateOn(new DateOnly(2020, 7, 1)).ShouldBe(15m);
        code.RateOn(new DateOnly(2026, 8, 27)).ShouldBe(15m);

        // Before the code existed at all, which is different from a rate of nothing.
        code.RateOn(new DateOnly(2017, 12, 31)).ShouldBeNull();
    }

    [Fact]
    public void Exempt_and_zero_rated_charge_nothing_whatever_rates_are_on_file()
    {
        foreach (var kind in new[] { TaxKind.ZeroRated, TaxKind.Exempt })
        {
            var code = new TaxCode
            {
                TenantId = Guid.Empty,
                CompanyId = Guid.Empty,
                Code = kind.ToString(),
                Description = kind.ToString(),
                Kind = kind,
            };

            // A stray rate row must not make an exempt supply taxable.
            code.Rates.Add(new TaxRate { StartingDate = new DateOnly(2020, 1, 1), Percentage = 15m });

            code.RateOn(new DateOnly(2026, 8, 27)).ShouldBe(0m);
        }
    }
}
