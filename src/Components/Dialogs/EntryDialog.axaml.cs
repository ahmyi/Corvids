using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Corvids.Models;
using Corvids.Services;

namespace Corvids;

/// <summary>Add / edit dialog. Returns a new AppEntry (or null when cancelled) via ShowDialog.</summary>
public partial class EntryDialog : Window
{
    private readonly Guid _id;
    private readonly IReadOnlyList<ShellOption> _shells = Shell.Options;

    public EntryDialog() : this(null) { }

    public EntryDialog(AppEntry? existing)
    {
        InitializeComponent();
        _id = existing?.Id ?? Guid.NewGuid();
        Title = existing is null ? "Add app" : "Edit app";

        ShellBox.ItemsSource = _shells.Select(o => o.Available ? o.Label : $"{o.Label}  (not installed)").ToList();
        ShellBox.SelectedIndex = 0;
        ShellBox.SelectionChanged += (_, _) => UpdateShellHint();

        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            DirectoryBox.Text = existing.WorkingDirectory;
            CommandBox.Text = existing.Command;
            AutoStartBox.IsChecked = existing.AutoStart;
            AutoRestartBox.IsChecked = existing.AutoRestart;
            EnvironmentBox.Text = existing.Environment;
            CustomShellBox.Text = existing.CustomShell;
            var index = _shells.ToList().FindIndex(o => o.Kind == existing.Shell);
            ShellBox.SelectedIndex = index < 0 ? 0 : index;
            LoadSuggestions(existing.WorkingDirectory);
        }

        UpdateShellHint();
        Opened += (_, _) => NameBox.Focus();
    }

    private ShellOption SelectedShell => _shells[Math.Clamp(ShellBox.SelectedIndex, 0, _shells.Count - 1)];

    private void UpdateShellHint()
    {
        var option = SelectedShell;
        CustomShellPanel.IsVisible = option.Kind == ShellKind.Custom;

        ShellHint.Text = option.Kind switch
        {
            ShellKind.Auto when OperatingSystem.IsWindows() && Shell.GitBashPath is { } bash =>
                $"Runs through Git Bash at {bash}, so node/npm from nvm or your .bashrc are found.",
            ShellKind.Auto when OperatingSystem.IsWindows() =>
                "Git Bash was not found, so commands run through cmd.exe.",
            ShellKind.Auto => "Runs through your login shell ($SHELL -lc), so your profile's PATH applies.",
            ShellKind.GitBash => "bash -lc: loads ~/.bash_profile and ~/.bashrc first (nvm lives there).",
            ShellKind.Cmd => "Plain cmd.exe: only sees node/npm that are on the Windows PATH.",
            ShellKind.PowerShell => "Runs the command line with -Command, loading your PowerShell profile.",
            ShellKind.Wsl => "wsl.exe -e bash -lc: runs inside your default WSL distro with a Linux node.",
            ShellKind.Bash => "/bin/bash -lc",
            ShellKind.Zsh => "/bin/zsh -lc: note .zshrc is not loaded by a non-interactive zsh; use Custom "
                             + "with zsh -ic \"{cmd}\" if nvm lives there.",
            ShellKind.Custom => "Full command line for the shell; {cmd} is replaced with the app command.",
            _ => "",
        };
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the project folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (path is null) return;

        DirectoryBox.Text = path;
        LoadSuggestions(path);
    }

    private void DirectoryBox_LostFocus(object? sender, RoutedEventArgs e) =>
        LoadSuggestions(DirectoryBox.Text?.Trim().Trim('"') ?? "");

    private void LoadSuggestions(string directory)
    {
        if (!Directory.Exists(directory)) return;

        var info = PackageJsonReader.Read(directory);
        var commands = info?.Commands ?? Array.Empty<string>();
        SuggestionsList.ItemsSource = commands;
        SuggestionsPanel.IsVisible = commands.Count > 0;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = info?.Name ?? Path.GetFileName(directory.TrimEnd('\\', '/'));
    }

    private void SuggestionsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SuggestionsList.SelectedItem is string command) CommandBox.Text = command;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? "";
        var directory = DirectoryBox.Text?.Trim().Trim('"') ?? "";
        var command = CommandBox.Text?.Trim() ?? "";
        var shell = SelectedShell;
        var custom = CustomShellBox.Text?.Trim();

        if (name.Length == 0) { ShowError("Give the app a name."); return; }
        if (!Directory.Exists(directory)) { ShowError("The project folder does not exist."); return; }
        if (command.Length == 0) { ShowError("Enter the command that starts the app."); return; }
        if (!shell.Available) { ShowError($"{shell.Label} is not installed on this machine."); return; }
        if (shell.Kind == ShellKind.Custom && string.IsNullOrEmpty(custom))
        {
            ShowError("Enter the custom shell command line, e.g. C:\\msys64\\usr\\bin\\bash.exe -lc \"{cmd}\".");
            return;
        }

        Close(new AppEntry
        {
            Id = _id,
            Name = name,
            WorkingDirectory = directory,
            Command = command,
            AutoStart = AutoStartBox.IsChecked == true,
            AutoRestart = AutoRestartBox.IsChecked == true,
            Environment = string.IsNullOrWhiteSpace(EnvironmentBox.Text) ? null : EnvironmentBox.Text.Trim(),
            Shell = shell.Kind,
            CustomShell = shell.Kind == ShellKind.Custom ? custom : null,
        });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
