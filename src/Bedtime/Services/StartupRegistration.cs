using Microsoft.Win32;

namespace Bedtime.Services;

/// <summary>
/// "Start with Windows" via the per-user Run key. The registry is the source of truth, so the toggle also
/// reflects changes made in Settings › Apps › Startup or Task Manager.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Bedtime";

    public static bool IsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string)
            return false;

        // Settings and Task Manager disable startup apps by setting an odd first byte here.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            run.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
