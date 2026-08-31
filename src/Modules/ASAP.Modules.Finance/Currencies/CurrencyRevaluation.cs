namespace ASAP.Modules.Finance.Currencies;

/// <summary>
/// What an open foreign balance is worth at a closing rate, and what that costs.
/// </summary>
/// <remarks>
/// Kept apart from the service because it is the part worth being certain about, and because the
/// sign convention is the thing most easily got backwards. The rule: a receivable worth more in
/// riyals than it is carried at is a gain, and a payable worth more is a loss.
/// </remarks>
public static class CurrencyRevaluation
{
    /// <summary>
    /// The difference to post, signed the way the control account is short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Positive is a loss to the company, negative a gain — the same convention the settlement
    /// poster uses, so the two write into the same pair of accounts without either having to know
    /// about the other.
    /// </para>
    /// <para>
    /// Measured against what the balance is <em>carried at</em>, not against what it was worth
    /// when it was raised. That is what lets the same run be made twice on the same date and post
    /// nothing the second time: after the first, the carrying amount already is the closing
    /// valuation, and the difference is zero.
    /// </para>
    /// </remarks>
    /// <param name="remainingInCurrency">What is still owed, in the foreign currency.</param>
    /// <param name="carryingAmount">What that is carried at in the company's own currency.</param>
    /// <param name="multiplier">The closing rate, base per one unit of currency.</param>
    /// <returns>The difference, positive where the company is worse off.</returns>
    public static decimal Difference(
        decimal remainingInCurrency,
        decimal carryingAmount,
        decimal multiplier)
    {
        var revalued = Math.Round(remainingInCurrency * multiplier, 2, MidpointRounding.AwayFromZero);

        // Carrying less revalued. A receivable carried at 3,750 and worth 3,800 gives -50: the
        // control account goes up by fifty and the company is fifty better off.
        return carryingAmount - revalued;
    }

    /// <summary>What the balance is worth at the closing rate.</summary>
    /// <param name="remainingInCurrency">What is still owed, in the foreign currency.</param>
    /// <param name="multiplier">The closing rate, base per one unit of currency.</param>
    /// <returns>The revalued amount, in the company's own currency.</returns>
    public static decimal Revalued(decimal remainingInCurrency, decimal multiplier)
        => Math.Round(remainingInCurrency * multiplier, 2, MidpointRounding.AwayFromZero);
}
