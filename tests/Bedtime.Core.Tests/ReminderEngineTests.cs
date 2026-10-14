using static Bedtime.Core.Tests.TestClock;

namespace Bedtime.Core.Tests;

public class ReminderEngineTests
{
    // Everyday 7:00 with an 8 hr goal: heads-ups 22:00 and 22:45, bedtime 23:00, nudges every 15 mins.
    private readonly BedtimeSettings _settings = Everyday(7, 0);
    private static readonly TimeSpan Active = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Away = TimeSpan.FromMinutes(20);

    private static DateTimeOffset Evening(int hour, int minute = 0, int second = 0) =>
        Uk_(2026, 10, 14, hour, minute).AddSeconds(second);

    private ReminderDecision? Tick(ReminderEngine engine, DateTimeOffset now, TimeSpan? idle = null) =>
        engine.Evaluate(now, _settings, Uk, idle ?? Active).Reminder;

    [Fact]
    public void FirstEvaluation_NeverReplaysPastReminders()
    {
        var engine = new ReminderEngine();
        Assert.Null(Tick(engine, Evening(23, 5)));
    }

    [Fact]
    public void HeadsUp_FiresOnceWhenItsTimeIsCrossed()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(21, 59, 50));

        var reminder = Tick(engine, Evening(22, 0, 10));
        Assert.Equal(ReminderKind.HeadsUp, reminder?.Kind);

        Assert.Null(Tick(engine, Evening(22, 0, 30)));
        Assert.Null(Tick(engine, Evening(22, 10)));
    }

    [Fact]
    public void Bedtime_FiresAtBedtime()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(22, 59, 50));

        Assert.Equal(ReminderKind.Bedtime, Tick(engine, Evening(23, 0, 5))?.Kind);
    }

    [Fact]
    public void AfterSleep_OnlyTheLatestRecentReminderFires()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(21, 30));

        // PC slept from 21:30 to 23:05: 22:00, 22:45 and 23:00 all came due; only 23:00 should show.
        Assert.Equal(ReminderKind.Bedtime, Tick(engine, Evening(23, 5))?.Kind);
    }

    [Fact]
    public void AfterSleep_StaleRemindersAreDropped()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(21, 30));

        Assert.Null(Tick(engine, Evening(22, 12))); // 22:00 is 12 mins late
    }

    [Fact]
    public void Nudge_OnlyWhenThePcIsInUse()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(23, 14, 50));
        Assert.Null(Tick(engine, Evening(23, 15, 10), Away));

        Tick(engine, Evening(23, 29, 50));
        Assert.Equal(ReminderKind.Nudge, Tick(engine, Evening(23, 30, 10), Active)?.Kind);
    }

    [Fact]
    public void Snooze_HoldsScheduledRemindersAndFiresWhenItEnds()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(22, 44, 50));
        Assert.Equal(ReminderKind.HeadsUp, Tick(engine, Evening(22, 45, 5))?.Kind);

        engine.Snooze(Evening(22, 45, 10), 20);  // until 23:05:10, past bedtime

        Assert.Null(Tick(engine, Evening(23, 0, 5)));  // bedtime is held back by the snooze
        Assert.Null(Tick(engine, Evening(23, 5)));

        var reminder = Tick(engine, Evening(23, 5, 20));
        Assert.Equal(ReminderKind.Nudge, reminder?.Kind);
        Assert.Null(engine.SnoozeUntil);
    }

    [Fact]
    public void Silenced_MutesTonightButNotTomorrow()
    {
        var engine = new ReminderEngine();
        var tonight = SleepPlanner.GetUpcomingNight(Evening(21, 0), _settings, Uk)!;
        _settings.SilencedUntil = tonight.WakeAt;

        Tick(engine, Evening(22, 59, 50));
        Assert.Null(Tick(engine, Evening(23, 0, 5)));

        var tomorrowEvening = Uk_(2026, 10, 15, 22, 59).AddSeconds(50);
        Tick(engine, tomorrowEvening);
        Assert.Equal(ReminderKind.Bedtime, Tick(engine, tomorrowEvening.AddSeconds(15))?.Kind);
    }

    [Fact]
    public void EditingSettings_NeverFiresRemindersAlreadyInThePast()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(22, 30));

        _settings.SleepGoal = TimeSpan.FromHours(9); // bedtime jumps back to 22:00
        Assert.Null(Tick(engine, Evening(22, 30, 20)));
    }

    [Fact]
    public void NightChanged_IsReportedWhenTheWakeUpPasses()
    {
        var engine = new ReminderEngine();
        engine.Evaluate(Uk_(2026, 10, 15, 6, 59), _settings, Uk, Active);

        var result = engine.Evaluate(Uk_(2026, 10, 15, 7, 0).AddSeconds(5), _settings, Uk, Active);

        Assert.True(result.NightChanged);
        Assert.Equal(Uk_(2026, 10, 16, 7, 0), result.Night!.WakeAt);
    }

    [Fact]
    public void ClockSetBackwards_DoesNotFire()
    {
        var engine = new ReminderEngine();
        Tick(engine, Evening(23, 10));
        Assert.Null(Tick(engine, Evening(22, 50)));
        Assert.Null(Tick(engine, Evening(22, 50, 20)));
    }
}
