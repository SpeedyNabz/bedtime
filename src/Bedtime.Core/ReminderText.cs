using System.Globalization;

namespace Bedtime.Core;

public sealed record ToastText(string Title, string Body);

/// <summary>All user-facing wording, so the app and the tests share one source.</summary>
public static class ReminderText
{
    /// <summary>"8 hrs", "1 hr 15 mins", "45 mins" - the same style as the Windows Focus timer ("30 mins").</summary>
    public static string Duration(TimeSpan span)
    {
        var totalMinutes = (int)Math.Round(Math.Max(0, span.TotalMinutes), MidpointRounding.AwayFromZero);
        if (totalMinutes >= 24 * 60)
        {
            // Beyond a day, minutes are noise: "1 day 4 hrs".
            var totalHours = (int)Math.Round(totalMinutes / 60.0, MidpointRounding.AwayFromZero);
            var days = totalHours / 24;
            var dayText = days == 1 ? "1 day" : $"{days} days";
            return totalHours % 24 == 0 ? dayText : $"{dayText} {Hours(totalHours % 24)}";
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var minuteText = minutes == 1 ? "1 min" : $"{minutes} mins";

        if (hours == 0) return minuteText;
        return minutes == 0 ? Hours(hours) : $"{Hours(hours)} {minuteText}";
    }

    private static string Hours(int hours) => hours == 1 ? "1 hr" : $"{hours} hrs";

    /// <summary>Sleep goal for the stepper: "8 hrs", "7.5 hrs".</summary>
    public static string Goal(TimeSpan goal) =>
        goal.TotalHours % 1 == 0 ? Duration(goal) : $"{goal.TotalHours.ToString("0.#", CultureInfo.CurrentCulture)} hrs";

    public static string Time(DateTimeOffset at, CultureInfo culture) => at.ToString("t", culture);

    /// <summary>
    /// Splits a short time into the number and the AM/PM part, so the designator can be drawn smaller, as the
    /// taskbar clock flyout does ("8:37:52 PM"). Cultures with a 24-hour clock get an empty designator.
    /// </summary>
    public static (string Time, string Designator) SplitTime(DateTime time, CultureInfo culture)
    {
        var text = time.ToString("t", culture);
        var designator = time.Hour < 12 ? culture.DateTimeFormat.AMDesignator : culture.DateTimeFormat.PMDesignator;
        if (designator.Length > 0)
        {
            var index = text.IndexOf(designator, StringComparison.Ordinal);
            if (index >= 0)
                return (text.Remove(index, designator.Length).Trim(), designator);
        }
        return (text, "");
    }

    /// <summary>"tomorrow", "on Saturday", or empty when the wake-up is later today.</summary>
    public static string DayPhrase(DateTimeOffset wake, DateTimeOffset now, CultureInfo culture)
    {
        var days = (DateOnly.FromDateTime(wake.DateTime).DayNumber - DateOnly.FromDateTime(now.DateTime).DayNumber);
        return days switch
        {
            <= 0 => "",
            1 => "tomorrow",
            _ => "on " + culture.DateTimeFormat.GetDayName(wake.DayOfWeek),
        };
    }

    public static ToastText Toast(ReminderKind kind, Night night, DateTimeOffset now, BedtimeSettings settings, CultureInfo culture)
    {
        var wakeTime = Time(night.WakeAt, culture);
        switch (kind)
        {
            case ReminderKind.HeadsUp:
                var day = DayPhrase(night.WakeAt, now, culture);
                return new ToastText(
                    $"Bedtime in {Duration(night.BedtimeAt - now)}",
                    $"You're up at {wakeTime}{(day.Length > 0 ? " " + day : "")}. Start winding down to get {Duration(settings.SleepGoal)} of sleep.");

            case ReminderKind.Bedtime:
                return new ToastText(
                    "Time for bed",
                    $"Sleep now to get {Duration(night.WakeAt - now)} before your {wakeTime} wake-up.");

            default:
                return new ToastText(
                    "Still up?",
                    $"Only {Duration(night.WakeAt - now)} left until your {wakeTime} wake-up.");
        }
    }

    /// <summary>Tray icon tooltip (Windows allows up to 127 characters).</summary>
    public static string Tooltip(Night? night, DateTimeOffset now, bool silenced, CultureInfo culture)
    {
        if (night is null) return "Bedtime · No wake-up times set";
        if (silenced) return $"Bedtime · Reminders off until {Time(night.WakeAt, culture)}";
        return night.IsPastBedtime(now)
            ? $"Past bedtime · {Duration(night.WakeAt - now)} until {Time(night.WakeAt, culture)}"
            : $"Bedtime {Time(night.BedtimeAt, culture)} · in {Duration(night.BedtimeAt - now)}";
    }

    /// <summary>The line under the big bedtime in the panel: "Wake up tomorrow at 7:00 AM".</summary>
    public static string WakeLine(Night? night, DateTimeOffset now, CultureInfo culture)
    {
        if (night is null) return "Turn on a day below to get reminders";
        var day = DayPhrase(night.WakeAt, now, culture);
        return $"Wake up {(day.Length > 0 ? day + " " : "")}at {Time(night.WakeAt, culture)}";
    }

    /// <summary>The countdown line in the panel.</summary>
    public static string Status(Night? night, DateTimeOffset now, bool silenced)
    {
        if (night is null) return "No wake-up times set";
        if (silenced) return "Reminders off tonight";
        return night.IsPastBedtime(now)
            ? $"Past bedtime · {Duration(night.WakeAt - now)} left"
            : $"Bedtime in {Duration(night.BedtimeAt - now)}";
    }
}
