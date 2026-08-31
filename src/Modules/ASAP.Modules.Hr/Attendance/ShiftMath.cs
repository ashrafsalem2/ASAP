namespace ASAP.Modules.Hr.Attendance;

/// <summary>What a day at a shift came to.</summary>
/// <param name="WorkedMinutes">Minutes actually worked, less the shift's break.</param>
/// <param name="LateMinutes">Minutes late past the grace.</param>
/// <param name="EarlyLeaveMinutes">Minutes left before the shift ended.</param>
/// <param name="OvertimeMinutes">Minutes worked beyond the shift.</param>
public readonly record struct WorkedDay(
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes);

/// <summary>
/// What a clock-in and a clock-out come to against a shift.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the service because it is the part worth being sure about, and because the
/// awkward cases are all arithmetic: a night shift that ends the next morning, somebody who came
/// in early and left late, somebody who was late and stayed to make it up.
/// </para>
/// <para>
/// Late and overtime are both measured, and neither cancels the other. Somebody twenty minutes
/// late who stays an hour is twenty minutes late and has an hour of overtime, and reporting a net
/// forty is reporting a figure that answers no question anybody asked.
/// </para>
/// </remarks>
public static class ShiftMath
{
    /// <summary>Minutes from midnight, with a night shift's end counted into the next day.</summary>
    /// <param name="at">The time.</param>
    /// <param name="startsAt">When the shift starts.</param>
    /// <param name="crossesMidnight">Whether the shift finishes the next day.</param>
    /// <returns>Minutes since the shift's own midnight.</returns>
    public static int Minutes(TimeOnly at, TimeOnly startsAt, bool crossesMidnight)
    {
        var minutes = (int)(at - TimeOnly.MinValue).TotalMinutes;

        // A night shift's clock does not reset at midnight. Somebody starting at 22:00 and
        // leaving at 06:00 worked eight hours, not minus sixteen.
        if (crossesMidnight && at < startsAt)
        {
            minutes += 1440;
        }

        return minutes;
    }

    /// <summary>
    /// What one day came to.
    /// </summary>
    /// <param name="shift">The shift they were on.</param>
    /// <param name="clockedInAt">When they clocked in.</param>
    /// <param name="clockedOutAt">When they clocked out.</param>
    /// <returns>The minutes worked, late, early and over.</returns>
    public static WorkedDay Worked(Shift shift, TimeOnly clockedInAt, TimeOnly clockedOutAt)
    {
        ArgumentNullException.ThrowIfNull(shift);

        var crosses = shift.CrossesMidnight;

        var start = Minutes(shift.StartsAt, shift.StartsAt, crosses);
        var end = Minutes(shift.EndsAt, shift.StartsAt, crosses);
        var inAt = Minutes(clockedInAt, shift.StartsAt, crosses);
        var outAt = Minutes(clockedOutAt, shift.StartsAt, crosses);

        if (outAt < inAt)
        {
            // Out before in, once the night-shift wrap has been allowed for. Nothing sensible can
            // be said about it, and guessing is worse than saying nothing.
            return new WorkedDay(0, 0, 0, 0);
        }

        // Coming in early does not start the paid day early: the shift is what it is, and a shop
        // that paid from the moment somebody arrived would be paying for the queue at the gate.
        var paidFrom = Math.Max(inAt, start);
        var paidTo = Math.Min(outAt, end);

        var worked = Math.Max(0, paidTo - paidFrom - shift.BreakMinutes);
        var late = Math.Max(0, inAt - start - shift.GraceMinutes);
        var early = Math.Max(0, end - outAt);

        // Before the shift and after it both count. Somebody who came in an hour early to open up
        // worked that hour whether or not anybody asked them to.
        var over = Math.Max(0, start - inAt) + Math.Max(0, outAt - end);

        return new WorkedDay(worked, late, early, over);
    }
}
