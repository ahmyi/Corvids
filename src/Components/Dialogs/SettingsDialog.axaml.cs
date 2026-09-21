using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Corvids.Models;
using Corvids.Services;

namespace Corvids;

/// <summary>Application settings. Returns the saved AppSettings via ShowDialog, or null when cancelled.</summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog() : this(new AppSettings()) { }

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();

        RunAtStartupBox.Content = Autostart.PlatformLabel;
        RunAtStartupBox.IsChecked = current.RunAtStartup || Autostart.IsEnabled();
        StartMinimizedBox.IsChecked = current.StartMinimized;
        AppsFileBox.Text = ConfigStore.Location;
        TerminalBox.Text = current.TerminalCommand;

        // The former subtitles are now short, single-sentence tooltips on each setting.
        ToolTip.SetTip(RunAtStartupBox, "Runs Corvids automatically when you sign in.");
        var defaultApps = OperatingSystem.IsWindows() ? @"%APPDATA%\Corvids\apps.json" : "~/.config/Corvids/apps.json";
        var appsTip = $"Default is {defaultApps}.";
        ToolTip.SetTip(AppsFileLabel, appsTip);
        ToolTip.SetTip(AppsFileBox, appsTip);
        var terminalTip = OperatingSystem.IsWindows()
            ? "Empty uses Windows Terminal, then Git Bash, then cmd.exe."
            : "Empty uses your desktop's default terminal.";
        ToolTip.SetTip(TerminalLabel, terminalTip);
        ToolTip.SetTip(TerminalBox, terminalTip);

        TimestampBox.Text = current.TimestampFormat;
        TimestampLegend.Text = BuildLegend();
        TimestampBox.TextChanged += (_, _) => UpdateTimestampPreview();
        UpdateTimestampPreview();
    }

    private void UpdateTimestampPreview()
    {
        var pattern = string.IsNullOrWhiteSpace(TimestampBox.Text) ? TimeFormat.Default : TimestampBox.Text;
        TimestampPreview.Text = TimeFormat.Format(DateTime.Now, pattern);
    }

    /// <summary>Lays the date-time keys out three per line, monospace-aligned.</summary>
    private static string BuildLegend()
    {
        var sb = new System.Text.StringBuilder();
        var i = 0;
        foreach (var (token, meaning) in TimeFormat.Legend)
        {
            sb.Append($"{token} {meaning}".PadRight(14));
            if (++i % 5 == 0) sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose where the apps list is stored",
            SuggestedFileName = "apps.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });

        if (file?.TryGetLocalPath() is { } path) AppsFileBox.Text = path;
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var path = AppsFileBox.Text?.Trim().Trim('"');
        var dir = string.IsNullOrEmpty(path) ? SettingsStore.DefaultDir : Path.GetDirectoryName(path);
        if (dir is not null && Directory.Exists(dir)) Shell.RevealFolder(dir);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var appsFile = AppsFileBox.Text?.Trim().Trim('"');
        var defaultFile = Path.Combine(SettingsStore.DefaultDir, "apps.json");
        if (!string.IsNullOrEmpty(appsFile))
        {
            if (!appsFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("The apps config file must be a .json file.");
                return;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(appsFile));
            if (dir is null || !Directory.Exists(dir))
            {
                ShowError($"The folder does not exist: {dir}");
                return;
            }
        }

        var runAtStartup = RunAtStartupBox.IsChecked == true;
        if (Autostart.Set(runAtStartup) is { } problem)
        {
            ShowError($"Could not update run-at-startup: {problem}");
            return;
        }

        var isDefault = string.IsNullOrEmpty(appsFile) ||
            string.Equals(Path.GetFullPath(appsFile), Path.GetFullPath(defaultFile), StringComparison.OrdinalIgnoreCase);

        Close(new AppSettings
        {
            RunAtStartup = runAtStartup,
            StartMinimized = StartMinimizedBox.IsChecked == true,
            AppsFilePath = isDefault ? null : Path.GetFullPath(appsFile!),
            TerminalCommand = string.IsNullOrWhiteSpace(TerminalBox.Text) ? null : TerminalBox.Text.Trim(),
            TimestampFormat = string.IsNullOrWhiteSpace(TimestampBox.Text) ? null : TimestampBox.Text.Trim(),
        });
    }

    private void About_Click(object? sender, RoutedEventArgs e) => new SplashWindow(aboutMode: true).Show(this);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
