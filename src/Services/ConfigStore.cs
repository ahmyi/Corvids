using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corvids.Models;

namespace Corvids.Services;

/// <summary>
/// Loads and saves the list of registered apps (apps.json), and raises <see cref="ChangedExternally"/> when
/// something other than this process edits the file. Call <see cref="Configure"/> once at startup.
/// </summary>
public static class ConfigStore
{
    private static FileSystemWatcher? _watcher;
    private static string? _lastWritten;
    private static DateTime _lastEvent;

    /// <summary>Full path of apps.json. Defaults to the settings folder; Settings can point it elsewhere.</summary>
    public static string Location { get; private set; } = Path.Combine(SettingsStore.DefaultDir, "apps.json");

    /// <summary>Raised on a thread-pool thread after apps.json was modified by an external editor.</summary>
    public static event Action? ChangedExternally;

    /// <summary>Points the store at <paramref name="appsFilePath"/> (null = default) and (re)starts the watcher.</summary>
    public static void Configure(string? appsFilePath)
    {
        Location = string.IsNullOrWhiteSpace(appsFilePath)
            ? Path.Combine(SettingsStore.DefaultDir, "apps.json")
            : Path.GetFullPath(appsFilePath);
        _lastWritten = null;
        RestartWatcher();
    }

    public static List<AppEntry> Load()
    {
        if (!File.Exists(Location)) return new List<AppEntry>();
        try
        {
            var json = File.ReadAllText(Location);
            return JsonSerializer.Deserialize(json, AppConfigContext.Default.ListAppEntry) ?? new List<AppEntry>();
        }
        catch
        {
            return new List<AppEntry>();
        }
    }

    public static void Save(IEnumerable<AppEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
        var json = JsonSerializer.Serialize(entries.ToList(), AppConfigContext.Default.ListAppEntry);
        _lastWritten = json;
        File.WriteAllText(Location, json);
    }

    private static void RestartWatcher()
    {
        _watcher?.Dispose();
        var dir = Path.GetDirectoryName(Location)!;
        Directory.CreateDirectory(dir);

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(Location))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
    }

    private static async void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Editors fire several events per save; coalesce and let the writer finish.
        var stamp = _lastEvent = DateTime.UtcNow;
        await Task.Delay(400);
        if (stamp != _lastEvent) return;

        string content;
        try
        {
            content = File.ReadAllText(Location);
        }
        catch
        {
            return;
        }

        if (content == _lastWritten) return;
        ChangedExternally?.Invoke();
    }
}

/// <summary>Source-generated serializer: reflection-based JSON is disabled in Avalonia apps and breaks trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<AppEntry>))]
internal partial class AppConfigContext : JsonSerializerContext;
