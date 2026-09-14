using System.Runtime.CompilerServices;

// Store tests read and write single files (settings.json, apps.json), so keep them off each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Corvids.Tests;

/// <summary>
/// Redirects the config folder to a throwaway temp directory before any store type initializes, so tests
/// never touch the user's real %APPDATA%\Corvids. Runs once, before any test code.
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "corvids-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("CORVIDS_CONFIG_DIR", dir);
    }
}
