using Bedtime.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Bedtime.Services;

internal static class NotificationActions
{
    public const string Open = "open";
    public const string Snooze = "snooze";
    public const string Sleep = "sleep";
}

/// <summary>Native Windows notifications via the Windows App SDK, so they also land in the Notifications panel.</summary>
internal sealed class NotificationService
{
    private const string Tag = "bedtime";

    /// <summary>Raised on a background thread with the clicked action.</summary>
    public event EventHandler<string>? Invoked;

    public bool IsRegistered { get; private set; }

    public void Register()
    {
        try
        {
            // The handler must be attached before Register, or clicks launch a second process.
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register("Bedtime", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Bedtime.png")));
            IsRegistered = true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not register for notifications", ex);
        }
    }

    /// <summary>Shows a notification; returns its id once Windows has accepted it, or 0.</summary>
    public uint Show(ReminderKind kind, ToastText text, int snoozeMinutes)
    {
        if (!IsRegistered)
            return 0;

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", NotificationActions.Open)
                .AddText(text.Title)
                .AddText(text.Body)
                .SetTag(Tag)
                .SetGroup(Tag);

            if (kind != ReminderKind.HeadsUp)
            {
                // The Reminder scenario stays on screen until acted on and can break through Do Not Disturb
                // when "Show reminders" is allowed. Windows only honours it if there is a button.
                builder.SetScenario(AppNotificationScenario.Reminder)
                    .AddButton(new AppNotificationButton($"Snooze {ReminderText.Duration(TimeSpan.FromMinutes(snoozeMinutes))}")
                        .AddArgument("action", NotificationActions.Snooze))
                    .AddButton(new AppNotificationButton("Going to bed")
                        .AddArgument("action", NotificationActions.Sleep));
            }

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
            return notification.Id;
        }
        catch (Exception ex)
        {
            Log.Error("Could not show a notification", ex);
            return 0;
        }
    }

    /// <summary>Removes last night's leftovers from the Notifications panel.</summary>
    public async Task ClearAllAsync()
    {
        if (!IsRegistered)
            return;

        try
        {
            await AppNotificationManager.Default.RemoveAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not clear notifications", ex);
        }
    }

    public void Unregister()
    {
        if (!IsRegistered)
            return;

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            Log.Error("Could not unregister notifications", ex);
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var action = args.Arguments.TryGetValue("action", out var value) ? value : NotificationActions.Open;
        Invoked?.Invoke(this, action);
    }
}
