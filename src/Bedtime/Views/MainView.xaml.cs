using Bedtime.Controls;
using Bedtime.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bedtime.Views;

public sealed partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    public FlyoutViewModel ViewModel { get; private set; } = null!;

    public void Attach(FlyoutViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    private void OnDayChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is DayChip { Day: { } day })
            ViewModel.SelectedDay = day;
    }
}
