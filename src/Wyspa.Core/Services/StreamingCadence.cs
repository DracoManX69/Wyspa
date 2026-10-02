namespace Wyspa.Core.Services;

// Preserve two-hypothesis agreement. Spend less time idle between hypotheses,
// counting request/delivery time toward the cadence rather than adding to it.
public static class StreamingCadence
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    public static TimeSpan After(TimeSpan elapsed) =>
        TimeSpan.FromMilliseconds(Math.Max(100, Interval.TotalMilliseconds - elapsed.TotalMilliseconds));
}
