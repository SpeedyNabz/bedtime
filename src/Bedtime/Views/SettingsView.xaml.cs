using Bedtime.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bedtime.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public FlyoutViewModel ViewModel { get; private set; } = null!;

    public void Attach(FlyoutViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }
}
