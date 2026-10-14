using System.ComponentModel;
using Bedtime.Services;
using Bedtime.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;
using static Bedtime.Interop.NativeMethods;

namespace Bedtime;

/// <summary>
/// The tray panel, built to sit with the taskbar's own flyouts: same width and placement as the Calendar
/// flyout, acrylic, rounded corners, no title bar, hidden from the taskbar and Alt+Tab, and it closes when
/// you click elsewhere.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const double PanelWidth = 336; // effective pixels, like the Calendar flyout
    private const double EdgeGap = 12;     // effective pixels to the screen edge and the taskbar
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private readonly FlyoutViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly WinEventProc _onForegroundChanged; // must stay referenced while hooked
    private IntPtr _foregroundHook;
    private POINT _anchor;
    private DateTime _dismissedAt;
    private bool _closing;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        MainPage.Attach(viewModel);
        SettingsPage.Attach(viewModel);
        _hwnd = WindowNative.GetWindowHandle(this);

        AppWindow.Title = "Bedtime";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Bedtime.ico"));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var corners = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

        _onForegroundChanged = OnForegroundChanged;
        AppWindow.Closing += OnClosing;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyShellTheme();
    }

    public bool IsOpen => AppWindow.IsVisible;

    /// <summary>For the tray icon: a click while open closes it, including the click that just dismissed it.</summary>
    public void Toggle()
    {
        if (IsOpen)
            HideFlyout();
        else if (DateTime.UtcNow - _dismissedAt > ReopenGuard)
            ShowFlyout();
    }

    public void ShowFlyout()
    {
        if (_closing)
            return;

        GetCursorPos(out _anchor);
        ApplyShellTheme();
        Place();
        AppWindow.Show(activateWindow: true);
        Activate();
        SetForegroundWindow(_hwnd);
        PlayEntrance();

        // Light dismiss: close as soon as another app's window comes to the foreground (a click on the
        // desktop, the taskbar, another window, Alt+Tab...). Our own menus and drop-downs don't count.
        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                _onForegroundChanged, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        // The first time, layout only completes after the window is shown; refit once it has.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Place();
            FocusFirstElement();
        });
    }

    public void HideFlyout() => Hide(dismissed: false);

    public void CloseForExit()
    {
        _closing = true;
        Hide(dismissed: false);
        Close();
    }

    /// <summary>Follows the taskbar's light/dark mode, as the shell's own flyouts do.</summary>
    public void ApplyShellTheme()
    {
        var light = ShellTheme.IsLight();
        Root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;

        // Hairline border like the shell flyouts (COLORREF is 0x00BBGGRR).
        var border = light ? 0x00E0E0E0 : 0x00404040;
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private void Hide(bool dismissed)
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (!IsOpen)
            return;
        if (dismissed)
            _dismissedAt = DateTime.UtcNow;

        AppWindow.Hide();
        _viewModel.IsSettingsOpen = false; // reopen on the main page, like the shell flyouts
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time)
    {
        if (IsOpen)
            Hide(dismissed: true);
    }

    /// <summary>Sizes the window to its content and anchors it above the taskbar, at the clock end.</summary>
    private void Place()
    {
        var display = DisplayArea.GetFromPoint(new PointInt32(_anchor.X, _anchor.Y), DisplayAreaFallback.Primary);
        var monitor = MonitorFromPoint(_anchor, MONITOR_DEFAULTTONEAREST);
        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var work = display.WorkArea;
        var outer = display.OuterBounds;
        var gap = (int)Math.Round(EdgeGap * scale);
        var width = (int)Math.Ceiling(PanelWidth * scale);
        var height = Math.Min((int)Math.Ceiling(MeasureContentHeight() * scale), work.Height - 2 * gap);

        var x = work.X + work.Width - width - gap;
        var y = work.Y + work.Height - height - gap;
        if (work.X > outer.X) x = work.X + gap; // taskbar on the left
        if (work.Y > outer.Y) y = work.Y + gap; // taskbar at the top

        // The rect above is for the visible panel. The window rect also includes invisible resize borders,
        // so measure them (after moving onto the target monitor, as they scale with its DPI) and add them.
        AppWindow.Move(new PointInt32(x, y));
        var frame = GetFrameInsets();
        AppWindow.MoveAndResize(new RectInt32(
            x - frame.Left,
            y - frame.Top,
            width + frame.Left + frame.Right,
            height + frame.Top + frame.Bottom));
    }

    private RECT GetFrameInsets()
    {
        GetWindowRect(_hwnd, out var window);
        GetClientRect(_hwnd, out var client);
        var origin = new POINT();
        ClientToScreen(_hwnd, ref origin);
        return new RECT
        {
            Left = origin.X - window.Left,
            Top = origin.Y - window.Top,
            Right = window.Right - (origin.X + client.Right),
            Bottom = window.Bottom - (origin.Y + client.Bottom),
        };
    }

    private double MeasureContentHeight()
    {
        Root.Measure(new Windows.Foundation.Size(PanelWidth, double.PositiveInfinity));
        var height = Root.DesiredSize.Height;
        return height > 0 ? height : 480;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing)
            return;
        args.Cancel = true; // Alt+F4 just hides the panel; Quit is in the menu
        HideFlyout();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideFlyout();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FlyoutViewModel.IsSettingsOpen))
            return;

        var settings = _viewModel.IsSettingsOpen;
        MainPage.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;

        if (IsOpen)
        {
            Place();
            Animate(RootTranslate, "X", settings ? 24 : -24);
            FocusFirstElement();
        }
    }

    private void FocusFirstElement()
    {
        DependencyObject scope = _viewModel.IsSettingsOpen ? SettingsPage : MainPage;
        if (FocusManager.FindFirstFocusableElement(scope) is Control control)
            control.Focus(FocusState.Programmatic);
    }

    private void PlayEntrance() => Animate(RootTranslate, "Y", 16);

    private void Animate(TranslateTransform transform, string axis, double from)
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        var move = new DoubleAnimation { From = from, To = 0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = ease };
        Storyboard.SetTarget(move, transform);
        Storyboard.SetTargetProperty(move, axis);

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(167) };
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(move);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }
}
