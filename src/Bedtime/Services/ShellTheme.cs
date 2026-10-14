using Microsoft.Win32;

namespace Bedtime.Services;

/// <summary>
/// The taskbar, Start and the shell flyouts follow the "Windows mode" setting, which can differ from the
/// "app mode" that normal apps follow. Bedtime lives next to the clock, so it follows Windows mode.
/// </summary>
internal static class ShellTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
