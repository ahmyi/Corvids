using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Corvids.Services;

/// <summary>Registers / unregisters Corvids to launch at user sign-in, per platform.</summary>
public static class Autostart
{
    private const string AppName = "Corvids";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string PlatformLabel =>
        OperatingSystem.IsWindows() ? "Run Corvids when I sign in to Windows"
        : OperatingSystem.IsMacOS() ? "Run Corvids when I log in (LaunchAgent)"
        : "Run Corvids when I log in (XDG autostart)";

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows() is not null;
            return File.Exists(UnixFile());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns null on success, otherwise a message explaining why it failed.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WriteWindows(enabled);
            else if (enabled) File.WriteAllText(UnixFile(), UnixContent());
            else if (File.Exists(UnixFile())) File.Delete(UnixFile());
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The command line that relaunches this very build (single-file exe, or dotnet + dll).</summary>
    private static string LaunchCommand()
    {
        var exe = Environment.ProcessPath ?? "";
        var isDotnetHost = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        if (!isDotnetHost) return $"\"{exe}\"";

        var dll = Environment.GetCommandLineArgs().FirstOrDefault() ?? "";
        return $"\"{exe}\" \"{dll}\"";
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppName) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(AppName, LaunchCommand());
        else if (key.GetValue(AppName) is not null) key.DeleteValue(AppName);
    }

    private static string UnixFile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            var dir = Path.Combine(home, "Library", "LaunchAgents");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "com.corvids.app.plist");
        }

        var autostart = Path.Combine(home, ".config", "autostart");
        Directory.CreateDirectory(autostart);
        return Path.Combine(autostart, "corvids.desktop");
    }

    private static string UnixContent()
    {
        var exe = Environment.ProcessPath ?? "";
        if (OperatingSystem.IsMacOS())
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                   "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " +
                   "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                   "<plist version=\"1.0\"><dict>\n" +
                   "  <key>Label</key><string>com.corvids.app</string>\n" +
                   $"  <key>ProgramArguments</key><array><string>{exe}</string></array>\n" +
                   "  <key>RunAtLoad</key><true/>\n" +
                   "</dict></plist>\n";
        }

        return "[Desktop Entry]\nType=Application\nName=Corvids\n" +
               $"Exec={LaunchCommand()}\nX-GNOME-Autostart-enabled=true\n";
    }
}
