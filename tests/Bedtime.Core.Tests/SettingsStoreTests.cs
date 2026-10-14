namespace Bedtime.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bedtime-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var settings = new SettingsStore(_dir).Load();

        Assert.Equal(TimeSpan.FromHours(8), settings.SleepGoal);
        Assert.Equal(new TimeOnly(7, 0), settings[DayOfWeek.Monday].Time);
        Assert.Equal(new TimeOnly(9, 0), settings[DayOfWeek.Sunday].Time);
        Assert.Equal(new[] { 60, 15 }, settings.ReminderLeadMinutes);
        Assert.True(settings.NudgeAfterBedtime);
    }

    [Fact]
    public void RoundTrip()
    {
        var store = new SettingsStore(_dir);
        var settings = BedtimeSettings.CreateDefault();
        settings[DayOfWeek.Thursday].Time = new TimeOnly(6, 45);
        settings[DayOfWeek.Saturday].Enabled = false;
        settings.SleepGoal = TimeSpan.FromHours(7.5);
        settings.ReminderLeadMinutes = [120, 30];
        settings.SilencedUntil = new DateTimeOffset(2026, 10, 15, 7, 0, 0, TimeSpan.FromHours(1));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(new TimeOnly(6, 45), loaded[DayOfWeek.Thursday].Time);
        Assert.False(loaded[DayOfWeek.Saturday].Enabled);
        Assert.Equal(TimeSpan.FromHours(7.5), loaded.SleepGoal);
        Assert.Equal(new[] { 120, 30 }, loaded.ReminderLeadMinutes);
        Assert.Equal(settings.SilencedUntil, loaded.SilencedUntil);
        Assert.Contains("\"Thursday\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, "{ not json");

        var settings = store.Load();

        Assert.Equal(TimeSpan.FromHours(8), settings.SleepGoal);
        Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Normalize_RepairsHandEditedValues()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, """
            { "Days": { "Monday": { "Enabled": true, "Time": "06:00:45" } },
              "SleepGoal": "20:00:00", "ReminderLeadMinutes": [15, -5, 60, 15], "NudgeIntervalMinutes": 0 }
            """);

        var settings = store.Load();

        Assert.Equal(7, settings.Days.Count);
        Assert.Equal(new TimeOnly(6, 0), settings[DayOfWeek.Monday].Time);
        Assert.Equal(BedtimeSettings.MaxSleepGoal, settings.SleepGoal);
        Assert.Equal(new[] { 60, 15 }, settings.ReminderLeadMinutes);
        Assert.Equal(5, settings.NudgeIntervalMinutes);
    }
}
