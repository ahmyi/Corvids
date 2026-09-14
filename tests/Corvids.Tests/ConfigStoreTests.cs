using Corvids.Models;
using Corvids.Services;

namespace Corvids.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "corvids-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ConfigStore.Configure(Path.Combine(_dir, "apps.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        Assert.Empty(ConfigStore.Load());
    }

    [Fact]
    public void Save_then_Load_round_trips_all_fields()
    {
        var entry = new AppEntry
        {
            Name = "api",
            WorkingDirectory = @"E:\work\api",
            Command = "npm run dev",
            AutoStart = true,
            AutoRestart = true,
            Environment = "NODE_ENV=production",
            Shell = ShellKind.GitBash,
            CustomShell = null,
        };

        ConfigStore.Save(new[] { entry });
        var loaded = ConfigStore.Load();

        var only = Assert.Single(loaded);
        Assert.Equal(entry.Id, only.Id);
        Assert.Equal("api", only.Name);
        Assert.Equal(@"E:\work\api", only.WorkingDirectory);
        Assert.Equal("npm run dev", only.Command);
        Assert.True(only.AutoStart);
        Assert.True(only.AutoRestart);
        Assert.Equal("NODE_ENV=production", only.Environment);
        Assert.Equal(ShellKind.GitBash, only.Shell);
    }

    [Fact]
    public void Save_preserves_order_and_count()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => new AppEntry { Name = $"app{i}", WorkingDirectory = _dir, Command = "node ." })
            .ToList();

        ConfigStore.Save(entries);
        var loaded = ConfigStore.Load();

        Assert.Equal(new[] { "app0", "app1", "app2", "app3", "app4" }, loaded.Select(e => e.Name));
    }

    [Fact]
    public void Load_returns_empty_on_corrupt_json()
    {
        File.WriteAllText(ConfigStore.Location, "{ this is not valid json");

        Assert.Empty(ConfigStore.Load());
    }

    [Fact]
    public void Configure_with_null_falls_back_to_default_location()
    {
        ConfigStore.Configure(null);

        var configDir = Environment.GetEnvironmentVariable("CORVIDS_CONFIG_DIR")!;
        Assert.Equal(Path.Combine(configDir, "apps.json"), ConfigStore.Location);
    }
}
