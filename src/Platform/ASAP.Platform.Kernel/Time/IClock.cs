namespace ASAP.Platform.Kernel.Time;

/// <summary>
/// Supplies the current time. Everything in ASAP that needs "now" takes this rather than
/// calling <see cref="DateTime"/> directly, so posting dates, audit stamps and period checks
/// can be frozen in tests and driven by the company's own time zone in production.
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC. All persisted timestamps use this.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Today's date in the active company's time zone. Used for defaulting posting dates,
    /// which are calendar dates rather than instants.
    /// </summary>
    DateOnly Today { get; }
}
