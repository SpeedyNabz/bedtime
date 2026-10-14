namespace Bedtime.Core;

/// <summary>A reminder that should be shown now.</summary>
public sealed record ReminderDecision(ReminderKind Kind, Night Night, DateTimeOffset Now);

/// <param name="Night">The upcoming night, or null when no wake-up is enabled.</param>
/// <param name="Reminder">The reminder to show, if any.</param>
/// <param name="NightChanged">True when the upcoming night moved on (wake-up passed or schedule edited).</param>
public sealed record EngineResult(Night? Night, ReminderDecision? Reminder, bool NightChanged);

/// <summary>
/// Decides when to show reminders. Call <see cref="Evaluate"/> periodically with the wall-clock time; it
/// fires each reminder once, as its time is crossed, so sleep/resume and clock changes need no special timers.
/// </summary>
public sealed class ReminderEngine
{
    /// <summary>Reminders that come due while the PC is asleep are dropped once they are this late.</summary>
    public static readonly TimeSpan MaxLateness = TimeSpan.FromMinutes(10);

    /// <summary>"Still up?" nudges only show if the PC had input within this window.</summary>
    public static readonly TimeSpan ActiveThreshold = TimeSpan.FromMinutes(5);

    private DateTimeOffset? _lastEvaluated;
    private DateTimeOffset? _nightWake;

    public DateTimeOffset? SnoozeUntil { get; private set; }

    public void Snooze(DateTimeOffset now, int minutes) => SnoozeUntil = now.AddMinutes(minutes);

    public static bool IsSilenced(BedtimeSettings settings, Night night) =>
        settings.SilencedUntil is { } until && until >= night.WakeAt;

    public EngineResult Evaluate(DateTimeOffset now, BedtimeSettings settings, TimeZoneInfo zone, TimeSpan userIdle)
    {
        var night = SleepPlanner.GetUpcomingNight(now, settings, zone);

        var nightChanged = _nightWake != night?.WakeAt && _lastEvaluated is not null;
        if (nightChanged)
            SnoozeUntil = null;
        _nightWake = night?.WakeAt;

        // The first evaluation only sets the baseline, so starting the app never replays old reminders.
        // A clock set backwards is treated the same way.
        var since = _lastEvaluated is { } last && last <= now ? last : now;
        _lastEvaluated = now;

        if (night is null || IsSilenced(settings, night))
            return new EngineResult(night, null, nightChanged);

        if (SnoozeUntil is { } snoozeEnd)
        {
            if (snoozeEnd > now)
                return new EngineResult(night, null, nightChanged); // scheduled reminders wait out the snooze

            SnoozeUntil = null;
            var snoozed = now - snoozeEnd <= MaxLateness ? new ReminderDecision(KindAt(now, night), night, now) : null;
            return new EngineResult(night, snoozed, nightChanged);
        }

        var due = night.Events.LastOrDefault(e => e.At > since && e.At <= now);
        if (due is null || now - due.At > MaxLateness)
            return new EngineResult(night, null, nightChanged);

        if (due.Kind == ReminderKind.Nudge && userIdle > ActiveThreshold)
            return new EngineResult(night, null, nightChanged);

        return new EngineResult(night, new ReminderDecision(due.Kind, night, now), nightChanged);
    }

    private static ReminderKind KindAt(DateTimeOffset now, Night night)
    {
        if (now < night.BedtimeAt) return ReminderKind.HeadsUp;
        return now - night.BedtimeAt < TimeSpan.FromMinutes(1) ? ReminderKind.Bedtime : ReminderKind.Nudge;
    }
}
