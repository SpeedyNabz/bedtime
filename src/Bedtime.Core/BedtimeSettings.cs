namespace Bedtime.Core;

/// <summary>Wake-up setting for one day of the week. Reminders for it happen the night before.</summary>
public sealed class WakeDay
{
    public bool Enabled { get; set; } = true;
    public TimeOnly Time { get; set; } = new(7, 0);

    public WakeDay Clone() => new() { Enabled = Enabled, Time = Time };
}

public sealed class BedtimeSettings
{
    public const int CurrentSchemaVersion = 1;

    public static readonly TimeSpan MinSleepGoal = TimeSpan.FromHours(4);
    public static readonly TimeSpan MaxSleepGoal = TimeSpan.FromHours(12);
    public static readonly TimeSpan SleepGoalStep = TimeSpan.FromMinutes(30);

    /// <summary>Heads-up choices offered in Settings, in minutes before bedtime.</summary>
    public static readonly int[] LeadChoices = [120, 60, 30, 15];
    public static readonly int[] NudgeIntervalChoices = [10, 15, 20, 30];
    public static readonly int[] SnoozeChoices = [5, 10, 15];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<DayOfWeek, WakeDay> Days { get; set; } = CreateDefaultDays();

    /// <summary>How much sleep you want; bedtime is the wake-up time minus this.</summary>
    public TimeSpan SleepGoal { get; set; } = TimeSpan.FromHours(8);

    public List<int> ReminderLeadMinutes { get; set; } = [60, 15];
    public bool RemindAtBedtime { get; set; } = true;
    public bool NudgeAfterBedtime { get; set; } = true;
    public int NudgeIntervalMinutes { get; set; } = 15;
    public int SnoozeMinutes { get; set; } = 10;

    /// <summary>Reminders are muted for any night whose wake-up is at or before this instant.</summary>
    public DateTimeOffset? SilencedUntil { get; set; }

    public WakeDay this[DayOfWeek day] => Days[day];

    public static BedtimeSettings CreateDefault() => new();

    private static Dictionary<DayOfWeek, WakeDay> CreateDefaultDays()
    {
        var days = new Dictionary<DayOfWeek, WakeDay>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var weekend = day is DayOfWeek.Saturday or DayOfWeek.Sunday;
            days[day] = new WakeDay { Enabled = true, Time = weekend ? new TimeOnly(9, 0) : new TimeOnly(7, 0) };
        }
        return days;
    }

    /// <summary>Repairs values from an older or hand-edited file so the rest of the app can trust them.</summary>
    public void Normalize()
    {
        Days ??= [];
        var defaults = CreateDefaultDays();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (!Days.TryGetValue(day, out var wake) || wake is null)
                Days[day] = defaults[day];
        }

        if (SleepGoal < MinSleepGoal) SleepGoal = MinSleepGoal;
        if (SleepGoal > MaxSleepGoal) SleepGoal = MaxSleepGoal;

        ReminderLeadMinutes = (ReminderLeadMinutes ?? [])
            .Where(m => m > 0 && m <= 12 * 60)
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        NudgeIntervalMinutes = Math.Clamp(NudgeIntervalMinutes, 5, 120);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 60);
        SchemaVersion = CurrentSchemaVersion;
    }

    public BedtimeSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Days = Days.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        SleepGoal = SleepGoal,
        ReminderLeadMinutes = [.. ReminderLeadMinutes],
        RemindAtBedtime = RemindAtBedtime,
        NudgeAfterBedtime = NudgeAfterBedtime,
        NudgeIntervalMinutes = NudgeIntervalMinutes,
        SnoozeMinutes = SnoozeMinutes,
        SilencedUntil = SilencedUntil,
    };
}
