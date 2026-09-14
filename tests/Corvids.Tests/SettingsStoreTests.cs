using Corvids.Models;
using Corvids.Services;

namespace Corvids.Tests;

public class SettingsStoreTests : IDisposable
{
    public SettingsStoreTests() => DeleteSettingsFile();

    public void Dispose() => DeleteSettingsFile();

    private static void DeleteSettingsFile()
    {
        try { if (File.Exists(SettingsStore.Location)) File.Delete(SettingsStore.Location); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var settings = SettingsStore.Load();

        Assert.False(settings.RunAtStartup);
        Assert.False(settings.StartMinimized);
        Assert.Null(settings.AppsFilePath);
        Assert.Null(settings.TerminalCommand);
    }

    [Fact]
    public void Save_then_Load_round_trips_all_fields()
    {
        var settings = new AppSettings
        {
            RunAtStartup = true,
            StartMinimized = true,
            AppsFilePath = @"E:\configs\apps.json",
            TerminalCommand = "wt.exe -d \"{dir}\"",
        };

        SettingsStore.Save(settings);
        var loaded = SettingsStore.Load();

        Assert.True(loaded.RunAtStartup);
        Assert.True(loaded.StartMinimized);
        Assert.Equal(@"E:\configs\apps.json", loaded.AppsFilePath);
        Assert.Equal("wt.exe -d \"{dir}\"", loaded.TerminalCommand);
    }

    [Fact]
    public void Location_is_settings_json_inside_the_config_folder()
    {
        var configDir = Environment.GetEnvironmentVariable("CORVIDS_CONFIG_DIR")!;
        Assert.Equal(Path.Combine(configDir, "settings.json"), SettingsStore.Location);
    }
}
