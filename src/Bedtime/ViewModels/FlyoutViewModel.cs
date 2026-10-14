using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Bedtime.Core;
using Bedtime.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bedtime.ViewModels;

/// <summary>
/// State for the tray panel. Edits go straight into the shared <see cref="BedtimeSettings"/> and the host saves
/// and re-plans, like Windows Settings (no Save button).
/// </summary>
public sealed partial class FlyoutViewModel : ObservableObject
{
    private readonly BedtimeSettings _settings;
    private readonly IAppHost _host;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private Night? _night;

    internal FlyoutViewModel(BedtimeSettings settings, IAppHost host)
    {
        _settings = settings;
        _host = host;

        var first = (int)_culture.DateTimeFormat.FirstDayOfWeek;
        for (var i = 0; i < 7; i++)
            Days.Add(new DayChipViewModel((DayOfWeek)((first + i) % 7), _culture));

        NudgeIntervalOptions = [.. BedtimeSettings.NudgeIntervalChoices.Select(m => ReminderText.Duration(TimeSpan.FromMinutes(m)))];
        SnoozeOptions = [.. BedtimeSettings.SnoozeChoices.Select(m => ReminderText.Duration(TimeSpan.FromMinutes(m)))];
    }

    public ObservableCollection<DayChipViewModel> Days { get; } = [];

    public IReadOnlyList<string> NudgeIntervalOptions { get; }

    public IReadOnlyList<string> SnoozeOptions { get; }

    // ---- Header ---------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string BedtimeText { get; set; } = "";

    [ObservableProperty]
    public partial string BedtimeDesignator { get; set; } = "";

    [ObservableProperty]
    public partial string WakeLine { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasNight { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    // ---- Selected day ---------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDayTitle), nameof(SelectedDayEnabled), nameof(SelectedDayTime))]
    public partial DayChipViewModel? SelectedDay { get; set; }

    partial void OnSelectedDayChanged(DayChipViewModel? oldValue, DayChipViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    public string SelectedDayTitle => SelectedDay?.LongName ?? "";

    public bool SelectedDayEnabled
    {
        get => SelectedDay is { } day && _settings[day.Day].Enabled;
        set
        {
            if (SelectedDay is not { } day || _settings[day.Day].Enabled == value)
                return;
            _settings[day.Day].Enabled = value;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    public TimeSpan SelectedDayTime
    {
        get => SelectedDay is { } day ? _settings[day.Day].Time.ToTimeSpan() : TimeSpan.Zero;
        set
        {
            if (SelectedDay is not { } day || value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
                return;
            var time = TimeOnly.FromTimeSpan(value);
            if (_settings[day.Day].Time == time)
                return;
            _settings[day.Day].Time = time;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    /// <summary>Copies the selected day's wake-up to "weekdays", "weekend" or "all".</summary>
    [RelayCommand]
    private void CopyTo(string? target)
    {
        if (SelectedDay is not { } source)
            return;

        var wake = _settings[source.Day];
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var weekend = day is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var include = target switch { "weekdays" => !weekend, "weekend" => weekend, _ => true };
            if (!include || day == source.Day)
                continue;
            _settings[day].Enabled = wake.Enabled;
            _settings[day].Time = wake.Time;
        }
        _host.SettingsChanged();
    }

    // ---- Sleep goal -----------------------------------------------------------------------------------

    public string GoalText => ReminderText.Goal(_settings.SleepGoal);

    [RelayCommand(CanExecute = nameof(CanDecreaseGoal))]
    private void DecreaseGoal() => SetGoal(_settings.SleepGoal - BedtimeSettings.SleepGoalStep);

    private bool CanDecreaseGoal() => _settings.SleepGoal > BedtimeSettings.MinSleepGoal;

    [RelayCommand(CanExecute = nameof(CanIncreaseGoal))]
    private void IncreaseGoal() => SetGoal(_settings.SleepGoal + BedtimeSettings.SleepGoalStep);

    private bool CanIncreaseGoal() => _settings.SleepGoal < BedtimeSettings.MaxSleepGoal;

    private void SetGoal(TimeSpan goal)
    {
        _settings.SleepGoal = goal < BedtimeSettings.MinSleepGoal ? BedtimeSettings.MinSleepGoal
            : goal > BedtimeSettings.MaxSleepGoal ? BedtimeSettings.MaxSleepGoal
            : goal;
        OnPropertyChanged(nameof(GoalText));
        DecreaseGoalCommand.NotifyCanExecuteChanged();
        IncreaseGoalCommand.NotifyCanExecuteChanged();
        _host.SettingsChanged();
    }

    // ---- Skip tonight ---------------------------------------------------------------------------------

    public bool IsSkippedTonight
    {
        get => _night is not null && ReminderEngine.IsSilenced(_settings, _night);
        set
        {
            if (_night is null || value == IsSkippedTonight)
                return;
            _settings.SilencedUntil = value ? _night.WakeAt : null;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    // ---- Settings page --------------------------------------------------------------------------------

    public bool Lead120 { get => HasLead(120); set => SetLead(120, value); }

    public bool Lead60 { get => HasLead(60); set => SetLead(60, value); }

    public bool Lead30 { get => HasLead(30); set => SetLead(30, value); }

    public bool Lead15 { get => HasLead(15); set => SetLead(15, value); }

    private bool HasLead(int minutes) => _settings.ReminderLeadMinutes.Contains(minutes);

    private void SetLead(int minutes, bool on, [CallerMemberName] string? property = null)
    {
        if (HasLead(minutes) == on)
            return;
        if (on)
            _settings.ReminderLeadMinutes.Add(minutes);
        else
            _settings.ReminderLeadMinutes.Remove(minutes);
        _settings.ReminderLeadMinutes.Sort((a, b) => b.CompareTo(a));
        OnPropertyChanged(property);
        _host.SettingsChanged();
    }

    public bool RemindAtBedtime
    {
        get => _settings.RemindAtBedtime;
        set
        {
            if (_settings.RemindAtBedtime == value)
                return;
            _settings.RemindAtBedtime = value;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    public bool NudgeAfterBedtime
    {
        get => _settings.NudgeAfterBedtime;
        set
        {
            if (_settings.NudgeAfterBedtime == value)
                return;
            _settings.NudgeAfterBedtime = value;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    public int NudgeIntervalIndex
    {
        get => Array.IndexOf(BedtimeSettings.NudgeIntervalChoices, _settings.NudgeIntervalMinutes);
        set
        {
            if (value < 0 || value >= BedtimeSettings.NudgeIntervalChoices.Length)
                return;
            var minutes = BedtimeSettings.NudgeIntervalChoices[value];
            if (_settings.NudgeIntervalMinutes == minutes)
                return;
            _settings.NudgeIntervalMinutes = minutes;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    public int SnoozeIndex
    {
        get => Array.IndexOf(BedtimeSettings.SnoozeChoices, _settings.SnoozeMinutes);
        set
        {
            if (value < 0 || value >= BedtimeSettings.SnoozeChoices.Length)
                return;
            var minutes = BedtimeSettings.SnoozeChoices[value];
            if (_settings.SnoozeMinutes == minutes)
                return;
            _settings.SnoozeMinutes = minutes;
            OnPropertyChanged();
            _host.SettingsChanged();
        }
    }

    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled();
        set
        {
            try
            {
                StartupRegistration.SetEnabled(value);
            }
            catch (Exception ex)
            {
                Log.Error("Could not change the start-up registration", ex);
            }
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OnPropertyChanged(nameof(StartWithWindows)); // may have changed in Windows Settings meanwhile
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void SendTest() => _host.SendTestReminder();

    [RelayCommand]
    private void Quit() => _host.Quit();

    /// <summary>Refreshes everything derived from the plan; called after each scheduler evaluation.</summary>
    internal void Update(Night? night, DateTimeOffset now)
    {
        _night = night;
        HasNight = night is not null;

        if (night is not null)
            (BedtimeText, BedtimeDesignator) = ReminderText.SplitTime(night.BedtimeAt.DateTime, _culture);
        else
            (BedtimeText, BedtimeDesignator) = ("No bedtime", "");

        WakeLine = ReminderText.WakeLine(night, now, _culture);
        StatusText = ReminderText.Status(night, now, night is not null && ReminderEngine.IsSilenced(_settings, night));
        OnPropertyChanged(nameof(IsSkippedTonight));

        DayOfWeek? next = night?.WakeAt.DayOfWeek;
        foreach (var chip in Days)
            chip.Update(_settings[chip.Day], chip.Day == next, _culture);

        SelectedDay ??= Days.FirstOrDefault(d => d.Day == next) ?? Days[0];
    }
}
