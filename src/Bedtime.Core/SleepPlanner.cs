namespace Bedtime.Core;

public enum ReminderKind
{
    /// <summary>"Bedtime in 15 mins" - a heads-up before bedtime.</summary>
    HeadsUp,
    /// <summary>"Time for bed" - exactly at bedtime.</summary>
    Bedtime,
    /// <summary>"Still up?" - repeats after bedtime while the PC is in use.</summary>
    Nudge,
}

public sealed record ReminderEvent(DateTimeOffset At, ReminderKind Kind);

/// <summary>The night leading up to the next wake-up.</summary>
public sealed record Night(DateTimeOffset WakeAt, DateTimeOffset BedtimeAt, IReadOnlyList<ReminderEvent> Events)
{
    public bool IsPastBedtime(DateTimeOffset now) => now >= BedtimeAt;
}

public static class SleepPlanner
{
    private const int LookAheadDays = 8;

    /// <summary>"Still up?" nudges stop this long before the wake-up time.</summary>
    public static readonly TimeSpan NudgeCutoffBeforeWake = TimeSpan.FromMinutes(30);

    /// <summary>The first enabled wake-up strictly after <paramref name="now"/>, or null if every day is off.</summary>
    public static DateTimeOffset? GetNextWake(DateTimeOffset now, BedtimeSettings settings, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (var i = 0; i <= LookAheadDays; i++)
        {
            var date = today.AddDays(i);
            var day = settings[date.DayOfWeek];
            if (!day.Enabled)
                continue;

            var wake = ToInstant(date, day.Time, zone);
            if (wake > now)
                return wake;
        }
        return null;
    }

    public static Night? GetUpcomingNight(DateTimeOffset now, BedtimeSettings settings, TimeZoneInfo zone)
    {
        if (GetNextWake(now, settings, zone) is not { } wake)
            return null;

        // DateTimeOffset arithmetic works on absolute instants, so a night when the clocks change
        // still gets exactly SleepGoal hours. Converting back gives the right local offset for display.
        var bedtime = TimeZoneInfo.ConvertTime(wake - settings.SleepGoal, zone);

        var events = new List<ReminderEvent>();
        foreach (var lead in settings.ReminderLeadMinutes.Where(m => m > 0).Distinct().OrderByDescending(m => m))
            events.Add(new ReminderEvent(bedtime.AddMinutes(-lead), ReminderKind.HeadsUp));

        if (settings.RemindAtBedtime)
            events.Add(new ReminderEvent(bedtime, ReminderKind.Bedtime));

        if (settings.NudgeAfterBedtime && settings.NudgeIntervalMinutes > 0)
        {
            var last = wake - NudgeCutoffBeforeWake;
            for (var at = bedtime.AddMinutes(settings.NudgeIntervalMinutes); at <= last; at = at.AddMinutes(settings.NudgeIntervalMinutes))
                events.Add(new ReminderEvent(at, ReminderKind.Nudge));
        }

        return new Night(wake, bedtime, events);
    }

    /// <summary>
    /// Turns a local date and time into an instant. A time skipped when the clocks go forward moves to
    /// the first valid minute after it; a time that happens twice when they go back uses the first one.
    /// </summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
