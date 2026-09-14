using Corvids.Models;
using Corvids.Services;

namespace Corvids.Tests;

public class ShellTests
{
    [Fact]
    public void ForCommand_disables_ansi_colour_for_child_processes()
    {
        var psi = Shell.ForCommand("node .", ".", ShellKind.Custom, "sh -c \"{cmd}\"", out _);

        Assert.Equal("0", psi.Environment["FORCE_COLOR"]);
        Assert.Equal("1", psi.Environment["NO_COLOR"]);
    }

    [Fact]
    public void ForCommand_merges_supplied_environment_variables()
    {
        var env = new Dictionary<string, string> { ["NODE_ENV"] = "production", ["PORT"] = "8080" };

        var psi = Shell.ForCommand("node .", ".", ShellKind.Custom, "sh -c \"{cmd}\"", out _, env);

        Assert.Equal("production", psi.Environment["NODE_ENV"]);
        Assert.Equal("8080", psi.Environment["PORT"]);
    }

    [Fact]
    public void ForCommand_custom_shell_splits_executable_and_substitutes_command()
    {
        var psi = Shell.ForCommand("npm run dev", ".", ShellKind.Custom, "mybash -lc \"{cmd}\"", out var label);

        Assert.Equal("mybash", psi.FileName);
        Assert.Contains("npm run dev", psi.Arguments);
        Assert.Equal("mybash", label);
    }

    [Fact]
    public void ForCommand_sets_the_working_directory()
    {
        var dir = Path.GetTempPath();

        var psi = Shell.ForCommand("node .", dir, ShellKind.Custom, "sh -c \"{cmd}\"", out _);

        Assert.Equal(dir, psi.WorkingDirectory);
    }

    [Fact]
    public void Options_offer_auto_and_custom_on_every_platform()
    {
        var kinds = Shell.Options.Select(o => o.Kind).ToList();

        Assert.Contains(ShellKind.Auto, kinds);
        Assert.Contains(ShellKind.Custom, kinds);
        Assert.All(Shell.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    [Fact]
    public void ForCommand_on_windows_uses_cmd_when_asked()
    {
        if (!OperatingSystem.IsWindows()) return;

        var psi = Shell.ForCommand("dir", @"C:\", ShellKind.Cmd, null, out var label);

        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Contains("dir", psi.Arguments);
        Assert.Equal("cmd.exe", label);
    }

    [Fact]
    public void ForCommand_output_and_error_are_redirected_for_capture()
    {
        var psi = Shell.ForCommand("node .", ".", ShellKind.Custom, "sh -c \"{cmd}\"", out _);

        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.False(psi.UseShellExecute);
    }
}
