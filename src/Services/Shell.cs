using System.Diagnostics;
using System.IO;
using Corvids.Models;

namespace Corvids.Services;

/// <summary>One choice in the "Run with" picker.</summary>
public record ShellOption(ShellKind Kind, string Label, bool Available);

/// <summary>Platform-specific bits: which consoles exist, how to run a command in one, how to reveal a folder.</summary>
public static class Shell
{
    private const string CommandPlaceholder = "{cmd}";

    /// <summary>Full path to Git Bash on Windows, or null when not installed / not on Windows.</summary>
    public static string? GitBashPath { get; } = FindGitBash();

    /// <summary>pwsh (PowerShell 7) when on PATH, else Windows PowerShell 5, else null.</summary>
    public static string? PowerShellPath { get; } = FindPowerShell();

    public static string? WslPath { get; } = FindWsl();

    /// <summary>Consoles offered in the picker for this OS, in display order.</summary>
    public static IReadOnlyList<ShellOption> Options { get; } = BuildOptions();

    /// <summary>Builds a ProcessStartInfo that runs <paramref name="commandLine"/> through the chosen console.</summary>
    /// <param name="shellLabel">Human-readable name of the console actually used, for the log header.</param>
    public static ProcessStartInfo ForCommand(string commandLine, string workingDirectory, ShellKind kind,
        string? customShell, out string shellLabel, IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        if (kind == ShellKind.Custom)
        {
            shellLabel = ApplyCustom(psi, commandLine, customShell);
        }
        else if (OperatingSystem.IsWindows())
        {
            shellLabel = ApplyWindows(psi, commandLine, kind);
        }
        else
        {
            shellLabel = ApplyUnix(psi, commandLine, kind);
        }

        // Most Node tools honour these and skip ANSI colour codes, which keeps the log view clean.
        psi.Environment["FORCE_COLOR"] = "0";
        psi.Environment["NO_COLOR"] = "1";

        if (environment is not null)
        {
            foreach (var (key, value) in environment) psi.Environment[key] = value;
        }

        return psi;
    }

    private static string ApplyWindows(ProcessStartInfo psi, string commandLine, ShellKind kind)
    {
        if (kind == ShellKind.Auto) kind = GitBashPath is null ? ShellKind.Cmd : ShellKind.GitBash;

        switch (kind)
        {
            case ShellKind.GitBash or ShellKind.Bash or ShellKind.Zsh when GitBashPath is not null:
                // Login shell so ~/.bash_profile and ~/.bashrc run: that is where nvm, volta etc. put node on PATH.
                psi.FileName = GitBashPath;
                psi.ArgumentList.Add("-lc");
                psi.ArgumentList.Add(commandLine);
                return "Git Bash";

            case ShellKind.PowerShell when PowerShellPath is not null:
                psi.FileName = PowerShellPath;
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(commandLine);
                return Path.GetFileNameWithoutExtension(PowerShellPath);

            case ShellKind.Wsl when WslPath is not null:
                psi.FileName = WslPath;
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add("bash");
                psi.ArgumentList.Add("-lc");
                psi.ArgumentList.Add(commandLine);
                return "WSL bash";

            case ShellKind.Cmd:
                return ApplyCmd(psi, commandLine, "cmd.exe");

            default:
                return ApplyCmd(psi, commandLine, $"cmd.exe, {kind} not available");
        }
    }

    private static string ApplyCmd(ProcessStartInfo psi, string commandLine, string label)
    {
        psi.FileName = "cmd.exe";
        psi.Arguments = $"/d /s /c \"{commandLine}\"";
        return label;
    }

    private static string ApplyUnix(ProcessStartInfo psi, string commandLine, ShellKind kind)
    {
        var defaultShell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s ? s : "/bin/sh";
        var shell = kind switch
        {
            ShellKind.Bash when File.Exists("/bin/bash") => "/bin/bash",
            ShellKind.Zsh when File.Exists("/bin/zsh") => "/bin/zsh",
            ShellKind.PowerShell when PowerShellPath is not null => PowerShellPath,
            _ => defaultShell,
        };

        psi.FileName = shell;
        if (kind == ShellKind.PowerShell && shell == PowerShellPath)
        {
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-Command");
        }
        else
        {
            // Login shell so PATH additions from nvm / volta / homebrew in the user's profile are picked up.
            psi.ArgumentList.Add("-lc");
        }
        psi.ArgumentList.Add(commandLine);
        return Path.GetFileName(shell);
    }

    /// <summary>Custom template such as <c>C:\msys64\usr\bin\bash.exe -lc "{cmd}"</c>; {cmd} gets the command.</summary>
    private static string ApplyCustom(ProcessStartInfo psi, string commandLine, string? template)
    {
        template = template?.Trim();
        if (string.IsNullOrEmpty(template))
        {
            return OperatingSystem.IsWindows()
                ? ApplyCmd(psi, commandLine, "cmd.exe, custom shell is empty")
                : ApplyUnix(psi, commandLine, ShellKind.Auto) + ", custom shell is empty";
        }

        if (!template.Contains(CommandPlaceholder)) template += $" \"{CommandPlaceholder}\"";

        string exe, rest;
        if (template.StartsWith('"'))
        {
            var close = template.IndexOf('"', 1);
            exe = close > 0 ? template[1..close] : template.Trim('"');
            rest = close > 0 ? template[(close + 1)..] : "";
        }
        else
        {
            var space = template.IndexOf(' ');
            exe = space > 0 ? template[..space] : template;
            rest = space > 0 ? template[space..] : "";
        }

        psi.FileName = exe;
        psi.Arguments = rest.Trim().Replace(CommandPlaceholder, commandLine.Replace("\"", "\\\""));
        return Path.GetFileName(exe);
    }

    public static void RevealFolder(string directory)
    {
        var psi = new ProcessStartInfo { UseShellExecute = false };
        psi.FileName = OperatingSystem.IsWindows() ? "explorer.exe"
            : OperatingSystem.IsMacOS() ? "open"
            : "xdg-open";
        psi.ArgumentList.Add(directory);

        try { Process.Start(psi); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Opens an interactive terminal window in <paramref name="directory"/>. Uses <paramref name="customCommand"/>
    /// ({dir} replaced) when set, otherwise the platform default. Returns null on success, or an error message.
    /// </summary>
    public static string? OpenTerminal(string directory, string? customCommand)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customCommand))
            {
                RunDetached(customCommand.Replace("{dir}", directory), directory);
                return null;
            }

            if (OperatingSystem.IsWindows()) OpenWindowsTerminal(directory);
            else if (OperatingSystem.IsMacOS()) StartShell("open", new[] { "-a", "Terminal", directory }, null);
            else OpenLinuxTerminal(directory);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void OpenWindowsTerminal(string directory)
    {
        // Prefer Windows Terminal, then Git Bash (matches the default shell), then a plain cmd window.
        if (FindOnPath("wt.exe") is { } wt)
        {
            StartShell(wt, new[] { "-d", directory }, null);
            return;
        }

        if (GitBashLauncher() is { } gitBash)
        {
            StartShell(gitBash, new[] { $"--cd={directory}" }, null);
            return;
        }

        StartShell("cmd.exe", new[] { "/k", $"cd /d \"{directory}\"" }, directory, ownWindow: true);
    }

    /// <summary>git-bash.exe sits at the Git root, one level above bin\bash.exe. It opens its own mintty window.</summary>
    private static string? GitBashLauncher()
    {
        if (GitBashPath is null) return null;
        var gitRoot = Directory.GetParent(Path.GetDirectoryName(GitBashPath)!)?.FullName;
        var launcher = gitRoot is null ? null : Path.Combine(gitRoot, "git-bash.exe");
        return launcher is not null && File.Exists(launcher) ? launcher : null;
    }

    private static void OpenLinuxTerminal(string directory)
    {
        foreach (var (term, args) in new[]
        {
            ("x-terminal-emulator", Array.Empty<string>()),
            ("gnome-terminal", new[] { "--working-directory", directory }),
            ("konsole", new[] { "--workdir", directory }),
            ("xterm", Array.Empty<string>()),
        })
        {
            if (FindOnPath(term) is not { } path) continue;
            StartShell(path, args, directory);
            return;
        }

        throw new InvalidOperationException("No terminal emulator found (tried gnome-terminal, konsole, xterm).");
    }

    private static void StartShell(string fileName, string[] args, string? workingDirectory, bool ownWindow = false)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = true };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    /// <summary>Runs a free-form terminal command line (custom setting) as its own window.</summary>
    private static void RunDetached(string commandLine, string workingDirectory)
    {
        string exe, rest;
        commandLine = commandLine.Trim();
        if (commandLine.StartsWith('"'))
        {
            var close = commandLine.IndexOf('"', 1);
            exe = close > 0 ? commandLine[1..close] : commandLine.Trim('"');
            rest = close > 0 ? commandLine[(close + 1)..] : "";
        }
        else
        {
            var space = commandLine.IndexOf(' ');
            exe = space > 0 ? commandLine[..space] : commandLine;
            rest = space > 0 ? commandLine[space..] : "";
        }

        var psi = new ProcessStartInfo(exe)
        {
            Arguments = rest.Trim(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };
        Process.Start(psi);
    }

    // Detection

    private static IReadOnlyList<ShellOption> BuildOptions()
    {
        if (OperatingSystem.IsWindows())
        {
            var autoLabel = GitBashPath is null ? "Auto (cmd.exe, Git Bash not found)" : "Auto (Git Bash)";
            var psLabel = PowerShellPath is null ? "PowerShell"
                : Path.GetFileNameWithoutExtension(PowerShellPath) == "pwsh" ? "PowerShell 7 (pwsh)"
                : "Windows PowerShell";
            return new[]
            {
                new ShellOption(ShellKind.Auto, autoLabel, true),
                new ShellOption(ShellKind.GitBash, "Git Bash", GitBashPath is not null),
                new ShellOption(ShellKind.Cmd, "cmd.exe", true),
                new ShellOption(ShellKind.PowerShell, psLabel, PowerShellPath is not null),
                new ShellOption(ShellKind.Wsl, "WSL (bash)", WslPath is not null),
                new ShellOption(ShellKind.Custom, "Custom...", true),
            };
        }

        var defaultShell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s ? s : "/bin/sh";
        return new[]
        {
            new ShellOption(ShellKind.Auto, $"Auto ({Path.GetFileName(defaultShell)})", true),
            new ShellOption(ShellKind.Bash, "bash", File.Exists("/bin/bash")),
            new ShellOption(ShellKind.Zsh, "zsh", File.Exists("/bin/zsh")),
            new ShellOption(ShellKind.PowerShell, "PowerShell (pwsh)", PowerShellPath is not null),
            new ShellOption(ShellKind.Custom, "Custom...", true),
        };
    }

    private static string? FindGitBash()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("CORVIDS_BASH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Git", "bin", "bash.exe"),
        };
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return candidate;
        }

        // Fall back to PATH, skipping WSL's System32\bash.exe which is a different thing entirely.
        return FindOnPath("bash.exe", dir =>
            !dir.Contains("System32", StringComparison.OrdinalIgnoreCase) &&
            dir.Contains("Git", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindPowerShell()
    {
        var pwsh = FindOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (pwsh is not null) return pwsh;
        if (!OperatingSystem.IsWindows()) return null;

        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(legacy) ? legacy : null;
    }

    private static string? FindWsl()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        return File.Exists(wsl) ? wsl : null;
    }

    private static string? FindOnPath(string fileName, Func<string, bool>? dirFilter = null)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim();
            if (dirFilter is not null && !dirFilter(trimmed)) continue;

            var candidate = Path.Combine(trimmed, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
