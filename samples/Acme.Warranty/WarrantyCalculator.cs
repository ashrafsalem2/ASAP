using ASAP.Platform.Kernel.Time;

namespace Acme.Warranty;

/// <summary>What a warranty check came to.</summary>
/// <param name="DocumentNo">The sale asked about.</param>
/// <param name="SoldOn">The day it was sold.</param>
/// <param name="ExpiresOn">The last day it is covered.</param>
/// <param name="IsCovered">Whether it still is.</param>
/// <param name="DaysLeft">
/// How many days are left, or how many have passed as a negative. Signed rather than clamped,
/// because "expired three days ago" is a conversation a counter assistant can have and "expired"
/// is not.
/// </param>
public readonly record struct WarrantyStatus(
    string DocumentNo,
    DateOnly SoldOn,
    DateOnly ExpiresOn,
    bool IsCovered,
    int DaysLeft);

/// <summary>
/// Works out whether a sale is still under warranty.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is one line and the design is the rest. Counting in months rather than days is
/// the decision worth defending: a twelve-month warranty on something sold on 31 August runs to
/// 31 August, not to a date three hundred and sixty-five days later that lands on the thirtieth
/// in a leap year and produces an argument at a counter.
/// </para>
/// <para>
/// The last day is included. A customer arriving on the day the warranty expires is covered,
/// because that is what everybody outside a software company understands "twelve months" to mean.
/// </para>
/// </remarks>
/// <param name="clock">Supplies today, so this can be tested without waiting a year.</param>
public sealed class WarrantyCalculator(IClock clock)
{
    /// <summary>
    /// Works out where a sale stands.
    /// </summary>
    /// <param name="documentNo">The sale.</param>
    /// <param name="soldOn">The day it was sold.</param>
    /// <param name="months">How many months the warranty runs for.</param>
    /// <param name="on">The day to judge it on, or null for today.</param>
    /// <returns>Where it stands.</returns>
    public WarrantyStatus Check(string documentNo, DateOnly soldOn, int months, DateOnly? on = null)
    {
        var day = on ?? clock.Today;

        // Months, not days. Adding months clamps to the end of a shorter one, so something sold
        // on 31 January with a one-month warranty is covered to 28 February -- which is what a
        // customer expects and what a court would say.
        var expires = soldOn.AddMonths(Math.Max(months, 0));

        return new WarrantyStatus(
            documentNo,
            soldOn,
            expires,
            day <= expires,
            expires.DayNumber - day.DayNumber);
    }
}
