using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bedtime.Core;

/// <summary>Reads and writes settings.json. A missing or unreadable file never stops the app starting.</summary>
public sealed class SettingsStore
{
    public SettingsStore(string directory)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, "settings.json");
    }

    /// <summary>%LOCALAPPDATA%\Bedtime, or BEDTIME_DATA_DIR when set (handy for trying things without touching your settings).</summary>
    public static string DefaultDirectory { get; } =
        Environment.GetEnvironmentVariable("BEDTIME_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bedtime");

    public string Directory { get; }
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);

    public BedtimeSettings Load()
    {
        if (!File.Exists(FilePath))
            return BedtimeSettings.CreateDefault();

        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.BedtimeSettings)
                ?? throw new JsonException("settings.json is empty");
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Keep the broken file for inspection instead of silently overwriting it.
            var backup = Path.Combine(Directory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, backup, overwrite: true);
            return BedtimeSettings.CreateDefault();
        }
    }

    public void Save(BedtimeSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.BedtimeSettings));
        File.Move(temp, FilePath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BedtimeSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
