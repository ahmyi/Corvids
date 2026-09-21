using System.Text.Json.Serialization;

namespace Corvids.Models;

/// <summary>Which console/shell runs the command. Options not applicable to the current OS fall back to Auto.</summary>
public enum ShellKind
{
    /// <summary>Windows: Git Bash when installed, otherwise cmd.exe. macOS/Linux: $SHELL.</summary>
    Auto = 0,
    GitBash = 1,
    Cmd = 2,
    PowerShell = 3,
    Wsl = 4,
    Bash = 5,
    Zsh = 6,
    /// <summary>Uses <see cref="AppEntry.CustomShell"/>, a command line containing {cmd}.</summary>
    Custom = 7,
}

/// <summary>One registered Node.js application. Persisted to apps.json.</summary>
public class AppEntry
{
    /// <summary>Default update command when none is set on the entry.</summary>
    public const string DefaultUpdateCommand = "npm install";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Command { get; set; } = "";

    /// <summary>Command run by the "Update" button, in the app's folder. Empty uses <see cref="DefaultUpdateCommand"/>.</summary>
    public string? UpdateCommand { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>Restart automatically after an unexpected exit (like pm2's autorestart).</summary>
    public bool AutoRestart { get; set; }

    /// <summary>Extra environment variables, one KEY=VALUE per line. Lines starting with # are ignored.</summary>
    public string? Environment { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ShellKind>))]
    public ShellKind Shell { get; set; } = ShellKind.Auto;

    /// <summary>For ShellKind.Custom: e.g. <c>C:\msys64\usr\bin\bash.exe -lc "{cmd}"</c>.</summary>
    public string? CustomShell { get; set; }

    /// <summary>Parses <see cref="Environment"/> into key/value pairs.</summary>
    public Dictionary<string, string> ParseEnvironment()
    {
        var result = new Dictionary<string, string>();
        foreach (var raw in (Environment ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[1..^1];
            result[line[..eq].Trim()] = value;
        }

        return result;
    }
}
