using ASAP.Platform.Kernel.Time;

namespace ASAP.Platform.Core.Time;

/// <summary>
/// The ordinary clock, reading the machine time.
/// </summary>
/// <param name="timeZoneId">
/// IANA time zone that decides what "today" means. Defaults to Riyadh, which is where the first
/// deployment runs; a tenant elsewhere overrides it through its own setup.
/// </param>
public sealed class SystemClock(string timeZoneId = "Asia/Riyadh") : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateOnly Today
    {
        get
        {
            // A posting date is a calendar date, not an instant. At 02:00 in Riyadh it is still
            // yesterday in UTC, and defaulting a posting date from UTC would file the entry on the
            // wrong day -- and at a month boundary, in the wrong period.
            var zone = ResolveTimeZone(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A misconfigured zone must not take the system down. UTC is a defensible fallback,
            // and the misconfiguration shows up as a posting date a few hours out rather than as
            // a host that will not start.
            return TimeZoneInfo.Utc;
        }
    }
}
