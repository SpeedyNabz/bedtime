using static Bedtime.Core.Tests.TestClock;

namespace Bedtime.Core.Tests;

public class SleepPlannerTests
{
    [Fact]
    public void Evening_PlansForTomorrowMorning()
    {
        var s = BedtimeSettings.CreateDefault(); // weekdays 7:00, weekends 9:00, 8 hrs
        var now = Uk_(2026, 10, 14, 20, 37);    // Wednesday evening

        var night = SleepPlanner.GetUpcomingNight(now, s, Uk)!;

        Assert.Equal(Uk_(2026, 10, 15, 7, 0), night.WakeAt);   // Thursday
        Assert.Equal(Uk_(2026, 10, 14, 23, 0), night.BedtimeAt);
    }

    [Fact]
    public void AfterMidnight_StillPlansForThisMorning()
    {
        var s = BedtimeSettings.CreateDefault();
        var now = Uk_(2026, 10, 15, 1, 0);

        var night = SleepPlanner.GetUpcomingNight(now, s, Uk)!;

        Assert.Equal(Uk_(2026, 10, 15, 7, 0), night.WakeAt);
        Assert.True(night.IsPastBedtime(now));
    }

    [Fact]
    public void JustAfterWaking_MovesToTheNextNight()
    {
        var s = BedtimeSettings.CreateDefault();
        var now = Uk_(2026, 10, 15, 7, 0).AddSeconds(1);

        var night = SleepPlanner.GetUpcomingNight(now, s, Uk)!;

        Assert.Equal(Uk_(2026, 10, 16, 7, 0), night.WakeAt);
    }

    [Fact]
    public void DisabledDays_AreSkipped()
    {
        var s = BedtimeSettings.CreateDefault();
        s.Days[DayOfWeek.Saturday].Enabled = false;
        var now = Uk_(2026, 10, 16, 21, 0); // Friday evening

        var night = SleepPlanner.GetUpcomingNight(now, s, Uk)!;

        Assert.Equal(Uk_(2026, 10, 18, 9, 0), night.WakeAt); // Sunday
        Assert.Equal(Uk_(2026, 10, 18, 1, 0), night.BedtimeAt);
    }

    [Fact]
    public void NoEnabledDays_MeansNoNight()
    {
        var s = Only();
        Assert.Null(SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 20, 0), s, Uk));
    }

    [Fact]
    public void BedtimeCrossesMidnight()
    {
        var s = Everyday(6, 30);
        s.SleepGoal = TimeSpan.FromHours(7.5);

        var night = SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 12, 0), s, Uk)!;

        Assert.Equal(Uk_(2026, 10, 14, 23, 0), night.BedtimeAt);
    }

    [Fact]
    public void EarlyWake_BedtimeIsTheEveningBefore()
    {
        var s = Everyday(4, 0);

        var night = SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 12, 0), s, Uk)!;

        Assert.Equal(Uk_(2026, 10, 15, 4, 0), night.WakeAt);
        Assert.Equal(Uk_(2026, 10, 14, 20, 0), night.BedtimeAt);
    }

    [Fact]
    public void ClocksGoBack_StillExactlyEightHours()
    {
        // Sunday 25 Oct 2026: 02:00 BST becomes 01:00 GMT.
        var s = Everyday(9, 0);
        var now = Uk_(2026, 10, 24, 20, 0);

        var night = SleepPlanner.GetUpcomingNight(now, s, Uk)!;

        Assert.Equal(TimeSpan.FromHours(8), night.WakeAt - night.BedtimeAt);
        Assert.Equal(TimeSpan.Zero, night.WakeAt.Offset);           // 9:00 GMT
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), night.BedtimeAt); // the second 1:00 AM
        Assert.Equal(TimeSpan.Zero, night.BedtimeAt.Offset);
    }

    [Fact]
    public void ClocksGoForward_StillExactlyEightHours()
    {
        // Sunday 28 Mar 2027: 01:00 GMT becomes 02:00 BST.
        var s = Everyday(8, 0);
        var now = Uk_(2027, 3, 27, 20, 0);

        var night = SleepPlanner.GetUpcomingNight(now, s, Uk)!;

        Assert.Equal(TimeSpan.FromHours(8), night.WakeAt - night.BedtimeAt);
        Assert.Equal(new DateTimeOffset(2027, 3, 27, 23, 0, 0, TimeSpan.Zero), night.BedtimeAt);
    }

    [Fact]
    public void WakeTimeInsideTheSkippedHour_MovesToTheFirstValidMinute()
    {
        var instant = SleepPlanner.ToInstant(new DateOnly(2027, 3, 28), new TimeOnly(1, 30), Uk);

        Assert.Equal(new DateTimeOffset(2027, 3, 28, 2, 0, 0, TimeSpan.FromHours(1)), instant);
    }

    [Fact]
    public void Events_DefaultSchedule()
    {
        var s = Everyday(7, 0); // leads 60 + 15, bedtime, nudges every 15 until 6:30
        var night = SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 12, 0), s, Uk)!;
        var bedtime = night.BedtimeAt;

        Assert.Equal(new ReminderEvent(bedtime.AddMinutes(-60), ReminderKind.HeadsUp), night.Events[0]);
        Assert.Equal(new ReminderEvent(bedtime.AddMinutes(-15), ReminderKind.HeadsUp), night.Events[1]);
        Assert.Equal(new ReminderEvent(bedtime, ReminderKind.Bedtime), night.Events[2]);

        var nudges = night.Events.Where(e => e.Kind == ReminderKind.Nudge).ToList();
        Assert.Equal(bedtime.AddMinutes(15), nudges[0].At);
        Assert.Equal(night.WakeAt - SleepPlanner.NudgeCutoffBeforeWake, nudges[^1].At);
        Assert.Equal(30, nudges.Count); // 23:15 .. 6:30 inclusive
        Assert.Equal(night.Events.OrderBy(e => e.At), night.Events);
    }

    [Fact]
    public void Events_RespectSwitchedOffReminders()
    {
        var s = Everyday(7, 0);
        s.ReminderLeadMinutes = [];
        s.RemindAtBedtime = false;
        s.NudgeAfterBedtime = false;

        var night = SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 12, 0), s, Uk)!;

        Assert.Empty(night.Events);
    }
}
