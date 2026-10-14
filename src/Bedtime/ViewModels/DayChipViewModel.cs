using System.Globalization;
using Bedtime.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bedtime.ViewModels;

/// <summary>One column of the week row: the day's name and its wake-up time.</summary>
public sealed partial class DayChipViewModel : ObservableObject
{
    public DayChipViewModel(DayOfWeek day, CultureInfo culture)
    {
        Day = day;
        ShortName = GetShortName(day, culture);
        LongName = culture.DateTimeFormat.GetDayName(day);
    }

    /// <summary>
    /// "Mo", "Tu"... like the calendar flyout. Some cultures' shortest names are single letters that repeat
    /// (T, T, S, S); those fall back to the first two letters of the abbreviated name.
    /// </summary>
    private static string GetShortName(DayOfWeek day, CultureInfo culture)
    {
        var shortest = culture.DateTimeFormat.ShortestDayNames;
        if (shortest.Distinct().Count() == 7)
            return shortest[(int)day];
        var abbreviated = culture.DateTimeFormat.AbbreviatedDayNames[(int)day];
        return abbreviated.Length > 2 ? abbreviated[..2] : abbreviated;
    }

    public DayOfWeek Day { get; }

    /// <summary>"Mo" - the same two-letter names as the calendar flyout.</summary>
    public string ShortName { get; }

    public string LongName { get; }

    [ObservableProperty]
    public partial string TimeText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDesignator))]
    public partial string Designator { get; set; } = "";

    public bool HasDesignator => Designator.Length > 0;

    [ObservableProperty]
    public partial bool IsDayEnabled { get; set; }

    /// <summary>The wake-up tonight's bedtime is for; drawn with the accent fill, like "today" in the calendar.</summary>
    [ObservableProperty]
    public partial bool IsNext { get; set; }

    /// <summary>The day shown in the editor below the week row.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string AccessibleName { get; set; } = "";

    internal void Update(WakeDay wake, bool isNext, CultureInfo culture)
    {
        IsDayEnabled = wake.Enabled;
        IsNext = isNext;
        if (wake.Enabled)
        {
            var (time, designator) = ReminderText.SplitTime(DateTime.Today.Add(wake.Time.ToTimeSpan()), culture);
            TimeText = time;
            Designator = designator;
            AccessibleName = $"{LongName}, wake up at {wake.Time.ToString("t", culture)}";
        }
        else
        {
            TimeText = "Off";
            Designator = "";
            AccessibleName = $"{LongName}, no wake-up time";
        }
    }
}
