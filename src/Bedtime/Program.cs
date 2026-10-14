using Bedtime.Interop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Bedtime;

public static class Program
{
    private const string InstanceKey = "Bedtime.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Contains("--unregister", StringComparer.OrdinalIgnoreCase))
        {
            // Used by the uninstaller: removes the notification registration (COM server and display info).
            try
            {
                AppNotificationManager.Default.UnregisterAll();
            }
            catch (Exception ex)
            {
                Log.Error("Could not remove the notification registration", ex);
            }
            return 0;
        }

        // One copy only: a second launch (Start menu, --quit, ...) is handed to the running one.
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!main.IsCurrent)
        {
            RedirectTo(main);
            return 0;
        }
        if (args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
            return 0; // nothing running to quit

        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

    private static void RedirectTo(AppInstance main)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();

        // This process was just started by the user, so it may pass on the right to take the foreground.
        NativeMethods.AllowSetForegroundWindow(main.ProcessId);

        // Redirecting must not block this STA thread, hence the event and CoWaitForMultipleObjects.
        var done = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
        try
        {
            Task.Run(() =>
            {
                try
                {
                    main.RedirectActivationToAsync(activation).AsTask().Wait(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    Log.Error("Could not hand the launch to the running Bedtime", ex);
                }
                finally
                {
                    NativeMethods.SetEvent(done);
                }
            });
            NativeMethods.CoWaitForMultipleObjects(0, 15_000, 1, [done], out _);
        }
        finally
        {
            NativeMethods.CloseHandle(done);
        }
    }
}
