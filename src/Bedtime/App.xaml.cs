using System.Globalization;
using Bedtime.Core;
using Bedtime.Interop;
using Bedtime.Services;
using Bedtime.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Bedtime;

public partial class App : Application, IAppHost
{
    private const uint MenuOpen = 1;
    private const uint MenuSkipTonight = 2;
    private const uint MenuSettings = 3;
    private const uint MenuQuit = 4;

    private readonly SettingsStore _store = new(SettingsStore.DefaultDirectory);
    private readonly NotificationService _notifications = new();
    private BedtimeSettings _settings = BedtimeSettings.CreateDefault();
    private DispatcherQueue _dispatcher = null!;
    private DispatcherQueueTimer _saveTimer = null!;
    private ReminderScheduler _scheduler = null!;
    private TrayIcon _tray = null!;
    private FlyoutWindow _flyout = null!;
    private FlyoutViewModel _viewModel = null!;
    private bool _quitting;

    public App()
    {
        InitializeComponent();

        // A background app shouldn't vanish over one bad event; log it and carry on.
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown; // keep running with the panel hidden

        var firstRun = !_store.Exists;
        _settings = _store.Load();
        if (firstRun)
            SaveNow();
        Log.Info($"Started{(firstRun ? " (first run)" : "")}: {Environment.ProcessPath} {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");

        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();

        _viewModel = new FlyoutViewModel(_settings, this);
        _flyout = new FlyoutWindow(_viewModel);

        _notifications.Invoked += (_, action) => _dispatcher.TryEnqueue(() => OnNotificationAction(action));
        _notifications.Register();

        _tray = new TrayIcon(_dispatcher);
        _tray.Invoked += (_, _) => _flyout.Toggle();
        _tray.ContextMenuRequested += (_, point) => ShowTrayMenu(point);
        _tray.ShellThemeChanged += (_, _) => _flyout.ApplyShellTheme();
        _tray.TimeChanged += (_, _) =>
        {
            TimeZoneInfo.ClearCachedData();
            _scheduler.Evaluate();
        };
        _tray.Show();

        _scheduler = new ReminderScheduler(_dispatcher, () => _settings);
        _scheduler.Evaluated += OnEvaluated;
        _scheduler.Start();

        AppInstance.GetCurrent().Activated += OnRedirectedActivation;

        HandleCommandLine(Environment.GetCommandLineArgs().Skip(1), showByDefault: true, firstRun);
    }

    /// <summary>
    /// --background: start hidden (used at sign-in). --test-toast: send a sample reminder. --quit: exit.
    /// Windows starts us with ----AppNotificationActivated: when a notification is clicked; the click itself
    /// arrives through NotificationInvoked.
    /// </summary>
    private void HandleCommandLine(IEnumerable<string> args, bool showByDefault, bool firstRun = false)
    {
        var list = args.Select(a => a.Trim().ToLowerInvariant()).ToList();
        if (list.Contains("--quit"))
        {
            Quit();
            return;
        }
        if (list.Contains("--test-toast"))
            SendTestReminder();

        var quiet = list.Contains("--background")
            || list.Contains("--test-toast")
            || list.Any(a => a.StartsWith("----appnotificationactivated", StringComparison.Ordinal));
        if (firstRun || (showByDefault && !quiet))
            _flyout.ShowFlyout();
    }

    private void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        // Someone launched Bedtime again (Start menu, shortcut, script); this runs on a background thread.
        var arguments = e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch ? launch.Arguments ?? "" : "";
        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(a => a.StartsWith('-'));
        _dispatcher.TryEnqueue(() => HandleCommandLine(args, showByDefault: true));
    }

    private void OnEvaluated(object? sender, EngineResult result)
    {
        var now = DateTimeOffset.Now;
        var culture = CultureInfo.CurrentCulture;

        if (result.NightChanged)
        {
            _ = _notifications.ClearAllAsync();
            if (_settings.SilencedUntil is { } until && until <= now)
            {
                _settings.SilencedUntil = null;
                ScheduleSave();
            }
        }

        if (result.Reminder is { } reminder)
        {
            var text = ReminderText.Toast(reminder.Kind, reminder.Night, reminder.Now, _settings, culture);
            var id = _notifications.Show(reminder.Kind, text, _settings.SnoozeMinutes);
            Log.Info($"Reminder {reminder.Kind} (notification {id}): {text.Title} / {text.Body}");
        }

        var silenced = result.Night is not null && ReminderEngine.IsSilenced(_settings, result.Night);
        _tray.SetTooltip(ReminderText.Tooltip(result.Night, now, silenced, culture));
        _viewModel.Update(result.Night, now);
    }

    private void OnNotificationAction(string action)
    {
        switch (action)
        {
            case NotificationActions.Snooze:
                _scheduler.Engine.Snooze(DateTimeOffset.Now, _settings.SnoozeMinutes);
                Log.Info($"Snoozed for {_settings.SnoozeMinutes} mins");
                _scheduler.Evaluate();
                break;
            case NotificationActions.Sleep:
                Log.Info("Going to bed: reminders off until the wake-up");
                SetSkipTonight(true);
                break;
            default:
                _flyout.ShowFlyout();
                break;
        }
    }

    private void ShowTrayMenu(NativeMethods.POINT point)
    {
        var night = _scheduler.Last?.Night;
        var skipped = night is not null && ReminderEngine.IsSilenced(_settings, night);
        TrayMenuItem[] items =
        [
            new(MenuOpen, "Open Bedtime", IsDefault: true),
            new(MenuSkipTonight, "Skip tonight", IsChecked: skipped),
            new(MenuSettings, "Settings"),
            TrayMenuItem.Separator,
            new(MenuQuit, "Quit Bedtime"),
        ];

        switch (_tray.ShowMenu(items, point))
        {
            case MenuOpen:
                _flyout.ShowFlyout();
                break;
            case MenuSkipTonight:
                SetSkipTonight(!skipped);
                break;
            case MenuSettings:
                _flyout.ShowFlyout();
                _viewModel.IsSettingsOpen = true;
                break;
            case MenuQuit:
                Quit();
                break;
        }
    }

    private void SetSkipTonight(bool skip)
    {
        if (_scheduler.Last?.Night is not { } night)
            return;
        _settings.SilencedUntil = skip ? night.WakeAt : null;
        SettingsChanged();
    }

    public void SettingsChanged()
    {
        ScheduleSave();
        _scheduler.Evaluate();
    }

    public void SendTestReminder()
    {
        var now = DateTimeOffset.Now;
        var night = SleepPlanner.GetUpcomingNight(now, _settings, TimeZoneInfo.Local);
        var body = night is null
            ? "Turn on a wake-up day to get bedtime reminders."
            : $"Tonight's bedtime is {ReminderText.Time(night.BedtimeAt, CultureInfo.CurrentCulture)}. Reminders will look like this.";
        var id = _notifications.Show(ReminderKind.Bedtime, new ToastText("Test reminder", body), _settings.SnoozeMinutes);
        Log.Info($"Sent a test reminder (notification {id})");
    }

    public void Quit()
    {
        if (_quitting)
            return;
        _quitting = true;
        Log.Info("Quitting");

        _scheduler.Stop();
        if (_saveTimer.IsRunning)
        {
            _saveTimer.Stop();
            SaveNow();
        }
        _tray.Dispose();
        _notifications.Unregister();
        _flyout.CloseForExit();
        Exit();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
