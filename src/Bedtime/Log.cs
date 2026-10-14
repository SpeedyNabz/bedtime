using Bedtime.Core;

namespace Bedtime;

/// <summary>A small rolling log in %LOCALAPPDATA%\Bedtime, mainly to see when reminders fired.</summary>
internal static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly string FilePath = Path.Combine(SettingsStore.DefaultDirectory, "bedtime.log");
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
