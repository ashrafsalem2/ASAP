namespace ASAP.Modules.Hr.Leave;

/// <summary>What a stretch of leave is paid at.</summary>
/// <param name="Days">How many days it covers.</param>
/// <param name="PaidDays">
/// The days converted to full-pay equivalents. Sixty days at three quarters is forty-five.
/// </param>
/// <param name="UnpaidDays">
/// The difference. What payroll deducts, and the figure somebody will ask about, so it is
/// reported rather than left to be worked out from the other two.
/// </param>
public readonly record struct LeavePay(decimal Days, decimal PaidDays, decimal UnpaidDays);

/// <summary>
/// Works out what leave is paid at, given how much of the same kind came before it.
/// </summary>
/// <remarks>
/// The bands are cumulative across the leave year, like tax bands across income. Somebody with
/// twenty-five days of sickness already this year who is ill for another ten gets five at full
/// pay and five at three quarters — not ten at full pay because this absence is short, and not
/// ten at three quarters because they are past the first band overall.
/// </remarks>
public static class LeavePayCalculator
{
    /// <summary>
    /// What a stretch of leave is paid at.
    /// </summary>
    /// <param name="policy">The rules for this kind of leave.</param>
    /// <param name="days">How many days this stretch covers.</param>
    /// <param name="daysAlreadyTaken">
    /// How many days of the same kind fall earlier in the same leave year. Where the bands do not
    /// vary, this changes nothing and can be left at zero.
    /// </param>
    /// <returns>The days, and how many of them are paid.</returns>
    public static LeavePay For(LeaveKindPolicy policy, decimal days, decimal daysAlreadyTaken = 0m)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (days <= 0m)
        {
            return new LeavePay(0m, 0m, 0m);
        }

        var paid = 0m;
        var consumed = daysAlreadyTaken < 0m ? 0m : daysAlreadyTaken;
        var remaining = days;
        var floor = consumed;

        foreach (var band in policy.PayBands)
        {
            if (remaining <= 0m)
            {
                break;
            }

            // How much of this band is still unused. A band that ended before the days already
            // taken contributes nothing to this stretch.
            var ceiling = band.UpToDays;

            if (ceiling is { } limit)
            {
                if (limit <= floor)
                {
                    continue;
                }

                var available = limit - floor;
                var inBand = Math.Min(available, remaining);

                paid += inBand * band.PaidFraction;
                remaining -= inBand;
                floor += inBand;

                continue;
            }

            // The last band runs to the end.
            paid += remaining * band.PaidFraction;
            remaining = 0m;
        }

        // Anything left over when the bands ran out is unpaid. A policy whose last band has a
        // ceiling is a policy that stops saying anything, and silence is not full pay.
        var paidDays = Round(paid);

        return new LeavePay(Round(days), paidDays, Round(days) - paidDays);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
