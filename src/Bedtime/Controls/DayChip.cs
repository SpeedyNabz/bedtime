using System.ComponentModel;
using Bedtime.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Bedtime.Controls;

/// <summary>
/// A day in the week row, drawn like a day in the taskbar calendar: the next wake-up gets the accent fill
/// (like "today") and the day being edited gets an accent ring (like a selected date).
/// </summary>
public sealed partial class DayChip : Button
{
    public static readonly DependencyProperty DayProperty = DependencyProperty.Register(
        nameof(Day), typeof(DayChipViewModel), typeof(DayChip),
        new PropertyMetadata(null, (d, e) => ((DayChip)d).OnDayChanged((DayChipViewModel?)e.OldValue, (DayChipViewModel?)e.NewValue)));

    public DayChipViewModel? Day
    {
        get => (DayChipViewModel?)GetValue(DayProperty);
        set => SetValue(DayProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates(useTransitions: false);
    }

    private void OnDayChanged(DayChipViewModel? oldDay, DayChipViewModel? newDay)
    {
        if (oldDay is not null) oldDay.PropertyChanged -= OnDayPropertyChanged;
        if (newDay is not null) newDay.PropertyChanged += OnDayPropertyChanged;
        UpdateStates(useTransitions: false);
    }

    private void OnDayPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateStates(useTransitions: true);

    private void UpdateStates(bool useTransitions)
    {
        if (Day is not { } day)
            return;

        VisualStateManager.GoToState(this, day.IsNext ? "Next" : day.IsDayEnabled ? "On" : "Off", useTransitions);
        VisualStateManager.GoToState(this, day.IsSelected ? "Selected" : "Unselected", useTransitions);
        AutomationProperties.SetName(this, day.AccessibleName);
    }
}
