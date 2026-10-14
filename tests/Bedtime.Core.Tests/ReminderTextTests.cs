using System.Globalization;
using static Bedtime.Core.Tests.TestClock;

namespace Bedtime.Core.Tests;

public class ReminderTextTests
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(480, "8 hrs")]
    [InlineData(60, "1 hr")]
    [InlineData(75, "1 hr 15 mins")]
    [InlineData(45, "45 mins")]
    [InlineData(1, "1 min")]
    [InlineData(450, "7 hrs 30 mins")]
    [InlineData(0, "0 mins")]
    [InlineData(1440, "1 day")]
    [InlineData(1700, "1 day 4 hrs")]
    [InlineData(2 * 1440 + 60, "2 days 1 hr")]
    public void Duration_FormatsLikeWindows(int minutes, string expected) =>
        Assert.Equal(expected, ReminderText.Duration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Duration_RoundsToTheNearestMinute() =>
        Assert.Equal("1 hr", ReminderText.Duration(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(40)));

    [Fact]
    public void Goal_ShowsHalfHours()
    {
        CultureInfo.CurrentCulture = Culture;
        Assert.Equal("8 hrs", ReminderText.Goal(TimeSpan.FromHours(8)));
        Assert.Equal("7.5 hrs", ReminderText.Goal(TimeSpan.FromHours(7.5)));
    }

    [Fact]
    public void DayPhrase()
    {
        var now = Uk_(2026, 10, 14, 21, 0); // Wednesday
        Assert.Equal("tomorrow", ReminderText.DayPhrase(Uk_(2026, 10, 15, 7, 0), now, Culture));
        Assert.Equal("on Saturday", ReminderText.DayPhrase(Uk_(2026, 10, 17, 9, 0), now, Culture));
        Assert.Equal("", ReminderText.DayPhrase(Uk_(2026, 10, 15, 7, 0), Uk_(2026, 10, 15, 1, 0), Culture));
    }

    [Fact]
    public void Toasts()
    {
        var s = Everyday(7, 0);
        var night = SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 12, 0), s, Uk)!;

        var headsUp = ReminderText.Toast(ReminderKind.HeadsUp, night, night.BedtimeAt.AddMinutes(-15), s, Culture);
        Assert.Equal("Bedtime in 15 mins", headsUp.Title);
        Assert.Equal("You're up at 7:00 AM tomorrow. Start winding down to get 8 hrs of sleep.", headsUp.Body);

        var bedtime = ReminderText.Toast(ReminderKind.Bedtime, night, night.BedtimeAt, s, Culture);
        Assert.Equal("Time for bed", bedtime.Title);
        Assert.Equal("Sleep now to get 8 hrs before your 7:00 AM wake-up.", bedtime.Body);

        var nudge = ReminderText.Toast(ReminderKind.Nudge, night, night.BedtimeAt.AddMinutes(30), s, Culture);
        Assert.Equal("Still up?", nudge.Title);
        Assert.Equal("Only 7 hrs 30 mins left until your 7:00 AM wake-up.", nudge.Body);
    }

    [Fact]
    public void TooltipAndStatus()
    {
        var s = Everyday(7, 0);
        var night = SleepPlanner.GetUpcomingNight(Uk_(2026, 10, 14, 12, 0), s, Uk)!;
        var before = night.BedtimeAt.AddMinutes(-128);
        var after = night.BedtimeAt.AddMinutes(10);

        Assert.Equal("Bedtime 11:00 PM · in 2 hrs 8 mins", ReminderText.Tooltip(night, before, false, Culture));
        Assert.Equal("Past bedtime · 7 hrs 50 mins until 7:00 AM", ReminderText.Tooltip(night, after, false, Culture));
        Assert.Equal("Bedtime · Reminders off until 7:00 AM", ReminderText.Tooltip(night, before, true, Culture));
        Assert.Equal("Bedtime · No wake-up times set", ReminderText.Tooltip(null, before, false, Culture));

        Assert.Equal("Bedtime in 2 hrs 8 mins", ReminderText.Status(night, before, false));
        Assert.Equal("Past bedtime · 7 hrs 50 mins left", ReminderText.Status(night, after, false));
        Assert.Equal("Reminders off tonight", ReminderText.Status(night, before, true));
        Assert.True(ReminderText.Tooltip(night, after, false, Culture).Length <= 127);
    }

    [Fact]
    public void SplitTime()
    {
        Assert.Equal(("11:00", "PM"), ReminderText.SplitTime(new DateTime(2026, 10, 14, 23, 0, 0), Culture));
        Assert.Equal(("7:05", "AM"), ReminderText.SplitTime(new DateTime(2026, 10, 15, 7, 5, 0), Culture));
        Assert.Equal(("23:00", ""), ReminderText.SplitTime(new DateTime(2026, 10, 14, 23, 0, 0), CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void WakeLine()
    {
        var s = BedtimeSettings.CreateDefault();
        var wednesday = Uk_(2026, 10, 14, 21, 0);
        Assert.Equal("Wake up tomorrow at 7:00 AM", ReminderText.WakeLine(SleepPlanner.GetUpcomingNight(wednesday, s, Uk), wednesday, Culture));

        s.Days[DayOfWeek.Saturday].Enabled = false;
        var friday = Uk_(2026, 10, 16, 21, 0);
        Assert.Equal("Wake up on Sunday at 9:00 AM", ReminderText.WakeLine(SleepPlanner.GetUpcomingNight(friday, s, Uk), friday, Culture));

        var earlyThursday = Uk_(2026, 10, 15, 1, 0);
        Assert.Equal("Wake up at 7:00 AM", ReminderText.WakeLine(SleepPlanner.GetUpcomingNight(earlyThursday, s, Uk), earlyThursday, Culture));
    }
}
