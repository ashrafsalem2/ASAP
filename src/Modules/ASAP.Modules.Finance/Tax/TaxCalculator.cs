namespace ASAP.Modules.Finance.Tax;

/// <summary>What a line comes to once tax is worked out.</summary>
/// <param name="Base">The taxable amount, before tax.</param>
/// <param name="Tax">The tax on it.</param>
/// <param name="Total">Base plus tax, which is what changes hands.</param>
public readonly record struct TaxAmounts(decimal Base, decimal Tax, decimal Total);

/// <summary>
/// Works out tax from an amount and a rate.
/// </summary>
/// <remarks>
/// <para>
/// Pure arithmetic, deliberately. Every rule here has a right answer that can be stated without a
/// database, and tax is the part of an ERP where being a halala out is not a rounding detail --
/// it is a return that does not reconcile.
/// </para>
/// <para>
/// The two directions matter more than they look. A wholesaler quotes 100 and adds tax, giving
/// 115. A shop prices the same goods at 115 on the shelf, tax already inside, and has to work
/// backwards to declare 15. Both are ordinary, both appear in the same company, and the shop case
/// is the one systems get wrong -- taking 15% of 115 gives 17.25, overstating the tax on every
/// sale the till makes.
/// </para>
/// </remarks>
public static class TaxCalculator
{
    /// <summary>
    /// Works out tax on an amount that does not yet include it.
    /// </summary>
    /// <param name="amount">The net amount, before tax.</param>
    /// <param name="percentage">The rate, so 15 means 15%.</param>
    /// <param name="decimals">Places to round the tax to.</param>
    /// <returns>The base, the tax and the total.</returns>
    public static TaxAmounts FromNet(decimal amount, decimal percentage, int decimals = 2)
    {
        var tax = Round(amount * percentage / 100m, decimals);

        return new TaxAmounts(amount, tax, amount + tax);
    }

    /// <summary>
    /// Works out the tax already inside an amount.
    /// </summary>
    /// <param name="amount">The gross amount, tax included.</param>
    /// <param name="percentage">The rate, so 15 means 15%.</param>
    /// <param name="decimals">Places to round to.</param>
    /// <returns>The base, the tax and the total.</returns>
    /// <remarks>
    /// The tax fraction of a gross amount is rate / (100 + rate), not rate / 100. At 15% that is
    /// 3/23 of the total, so 115.00 carries 15.00 of tax and not 17.25.
    /// </remarks>
    public static TaxAmounts FromGross(decimal amount, decimal percentage, int decimals = 2)
    {
        var tax = Round(amount * percentage / (100m + percentage), decimals);

        // The base is what is left, rather than being rounded independently. Rounding both and
        // hoping they add up is how a line's own figures come to disagree with its total.
        return new TaxAmounts(amount - tax, tax, amount);
    }

    /// <summary>
    /// Works out tax across several amounts sharing one rate.
    /// </summary>
    /// <param name="amounts">The line amounts.</param>
    /// <param name="percentage">The rate.</param>
    /// <param name="taxIncluded">Whether the amounts already include the tax.</param>
    /// <param name="decimals">Places to round to.</param>
    /// <returns>The totals for the document.</returns>
    /// <remarks>
    /// Taxed on the sum rather than line by line, because rounding each line separately and adding
    /// the results drifts from the figure on the invoice. Twenty lines of 0.33 at 15% round to
    /// 0.05 each, totalling 1.00, while the document total of 6.60 carries 0.99. The customer's
    /// arithmetic is the one that has to be right.
    /// </remarks>
    public static TaxAmounts ForDocument(
        IEnumerable<decimal> amounts,
        decimal percentage,
        bool taxIncluded = false,
        int decimals = 2)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var total = amounts.Sum();

        return taxIncluded
            ? FromGross(total, percentage, decimals)
            : FromNet(total, percentage, decimals);
    }

    /// <summary>
    /// Spreads a document's tax back across its lines without losing or inventing a halala.
    /// </summary>
    /// <param name="amounts">The line amounts, in order.</param>
    /// <param name="taxTotal">The tax the document as a whole carries.</param>
    /// <param name="decimals">Places to round each line to.</param>
    /// <returns>The tax for each line, summing exactly to <paramref name="taxTotal"/>.</returns>
    /// <remarks>
    /// Needed because the document total is the authoritative figure while the tax entries are
    /// recorded per line. Each line takes its rounded share and the last takes the remainder, so
    /// the parts always add to the whole. The alternative -- rounding each share independently --
    /// leaves a few halalas unaccounted for, which is exactly the difference somebody spends an
    /// afternoon looking for at the end of a quarter.
    /// </remarks>
    public static IReadOnlyList<decimal> Allocate(
        IReadOnlyList<decimal> amounts,
        decimal taxTotal,
        int decimals = 2)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        if (amounts.Count == 0)
        {
            return [];
        }

        var basis = amounts.Sum();

        if (basis == 0m)
        {
            // Nothing to apportion against. All of it goes on the first line rather than being
            // dropped, because dropping it would lose tax the document says it carries.
            var only = new decimal[amounts.Count];
            only[0] = taxTotal;
            return only;
        }

        var shares = new decimal[amounts.Count];
        var running = 0m;

        for (var index = 0; index < amounts.Count - 1; index++)
        {
            shares[index] = Round(taxTotal * amounts[index] / basis, decimals);
            running += shares[index];
        }

        shares[^1] = taxTotal - running;

        return shares;
    }

    private static decimal Round(decimal value, int decimals)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
