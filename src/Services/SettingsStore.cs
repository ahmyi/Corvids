using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corvids.Models;

namespace Corvids.Services;

/// <summary>Loads and saves settings.json in the default Corvids config folder.</summary>
public static class SettingsStore
{
    /// <summary>%APPDATA%\Corvids on Windows, ~/.config/Corvids elsewhere.</summary>
    public static string DefaultDir { get; } =
        Environment.GetEnvironmentVariable("CORVIDS_CONFIG_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Corvids");

    public static string Location => Path.Combine(DefaultDir, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(Location)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(Location);
            return JsonSerializer.Deserialize(json, SettingsContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DefaultDir);
        File.WriteAllText(Location, JsonSerializer.Serialize(settings, SettingsContext.Default.AppSettings));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsContext : JsonSerializerContext;
