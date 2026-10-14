using Bedtime.Core;

namespace Bedtime.Core.Tests;

internal static class TestClock
{
    public static readonly TimeZoneInfo Uk = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

    /// <summary>A local UK wall-clock time as an instant (first occurrence if ambiguous).</summary>
    public static DateTimeOffset Uk_(int year, int month, int day, int hour, int minute = 0) =>
        SleepPlanner.ToInstant(new DateOnly(year, month, day), new TimeOnly(hour, minute), Uk);

    /// <summary>Every day off except the ones given.</summary>
    public static BedtimeSettings Only(params (DayOfWeek Day, int Hour, int Minute)[] wakes)
    {
        var s = BedtimeSettings.CreateDefault();
        foreach (var day in Enum.GetValues<DayOfWeek>())
            s.Days[day].Enabled = false;
        foreach (var (day, hour, minute) in wakes)
            s.Days[day] = new WakeDay { Enabled = true, Time = new TimeOnly(hour, minute) };
        return s;
    }

    /// <summary>Every day enabled at the same time.</summary>
    public static BedtimeSettings Everyday(int hour, int minute = 0)
    {
        var s = BedtimeSettings.CreateDefault();
        foreach (var day in Enum.GetValues<DayOfWeek>())
            s.Days[day] = new WakeDay { Enabled = true, Time = new TimeOnly(hour, minute) };
        return s;
    }
}
