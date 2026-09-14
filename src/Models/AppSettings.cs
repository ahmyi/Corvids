namespace Corvids.Models;

/// <summary>Application-wide preferences. Persisted to settings.json next to the default apps.json.</summary>
public class AppSettings
{
    /// <summary>Register Corvids to launch when the user signs in (Run key / LaunchAgent / autostart .desktop).</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Hide the window into the tray right after launch; apps still auto-start.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Full path of the apps list file. Null means the default location.</summary>
    public string? AppsFilePath { get; set; }

    /// <summary>
    /// Command used by "Open terminal" to open a console in an app's folder. {dir} is replaced with the
    /// working directory. Null uses the platform default (Windows Terminal / Git Bash / cmd, Terminal.app, etc.).
    /// </summary>
    public string? TerminalCommand { get; set; }
}
