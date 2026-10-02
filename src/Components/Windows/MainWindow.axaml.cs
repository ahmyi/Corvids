using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Corvids.Models;
using Corvids.Services;
using Corvids.ViewModels;

namespace Corvids;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private ManagedApp? _selectedApp;
    private bool _autoScroll = true;
    private bool _wrapLines;
    private bool _forceClose;
    private int _portTick;
    private bool _portScanBusy;
    private readonly DispatcherTimer _uptimeTimer;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ManagedApp> Apps { get; } = new();
    public AppSettings Settings { get; private set; }
    public string ConfigPath => ConfigStore.Location;

    public string VersionText =>
        $"Corvids v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?"}";

    /// <summary>Bottom status-bar text: how many apps are registered and running.</summary>
    public string StatusSummary => Apps.Count == 0
        ? "No apps"
        : $"{Apps.Count(a => a.IsRunning)} of {Apps.Count} running";

    public ManagedApp? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (ReferenceEquals(_selectedApp, value)) return;
            if (_selectedApp is not null) _selectedApp.LinesAppended -= ScrollToEnd;
            _selectedApp = value;
            if (_selectedApp is not null) _selectedApp.LinesAppended += ScrollToEnd;
            OnPropertyChanged();
            Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
        }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set { if (_autoScroll == value) return; _autoScroll = value; OnPropertyChanged(); ScrollToEnd(); }
    }

    public bool WrapLines
    {
        get => _wrapLines;
        set
        {
            if (_wrapLines == value) return;
            _wrapLines = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LogWrapping));
            OnPropertyChanged(nameof(LogHorizontalScroll));
        }
    }

    public TextWrapping LogWrapping => WrapLines ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public ScrollBarVisibility LogHorizontalScroll =>
        WrapLines ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        Settings = SettingsStore.Load();
        TimeFormat.Pattern = Settings.TimestampFormat ?? TimeFormat.Default;
        ConfigStore.Configure(Settings.AppsFilePath);
        foreach (var entry in ConfigStore.Load()) AddManaged(entry);
        SelectedApp = Apps.FirstOrDefault();
        ConfigStore.ChangedExternally += () => Dispatcher.UIThread.Post(ReloadFromDisk);

        LogList.AddHandler(ScrollViewer.ScrollChangedEvent, LogList_ScrollChanged);
        LogList.KeyDown += LogList_KeyDown;
        // Tunnel so this runs before the ContextMenu opens: select the right-clicked row, or cancel on empty space.
        AppList.AddHandler(ContextRequestedEvent, AppList_ContextRequested, RoutingStrategies.Tunnel);
        Closing += OnClosing;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            SelectedApp?.RefreshUptime();
            if (++_portTick % 3 == 0) MonitorPorts(); // scan every ~3s; skipped if a scan is still running
        };
        _uptimeTimer.Start();

        // Opened fires again every time the window is shown from the tray; the startup work must run once.
        var startedUp = false;
        Opened += (_, _) =>
        {
            if (startedUp) return;
            startedUp = true;

            foreach (var app in Apps.Where(a => a.Entry.AutoStart)) app.Start();
            if (Settings.StartMinimized) Hide();
        };
    }

    // App list management

    private ManagedApp AddManaged(AppEntry entry)
    {
        var app = new ManagedApp(entry);
        app.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ManagedApp.Status)) UpdateTitle(); };
        Apps.Add(app);
        UpdateTitle();
        return app;
    }

    /// <summary>
    /// apps.json was edited outside Corvids: add new entries, update edited ones in place, drop removed ones
    /// (stopping them first). Running apps keep their process and log; only their settings change.
    /// </summary>
    private void ReloadFromDisk()
    {
        var fromDisk = ConfigStore.Load();
        var byId = fromDisk.ToDictionary(e => e.Id);
        var selectedId = SelectedApp?.Entry.Id;

        foreach (var app in Apps.Where(a => !byId.ContainsKey(a.Entry.Id)).ToList())
        {
            app.Stop();
            Apps.Remove(app);
        }

        foreach (var entry in fromDisk)
        {
            var existing = Apps.FirstOrDefault(a => a.Entry.Id == entry.Id);
            if (existing is null)
            {
                var added = AddManaged(entry);
                if (entry.AutoStart) added.Start();
                continue;
            }

            existing.Entry.Name = entry.Name;
            existing.Entry.WorkingDirectory = entry.WorkingDirectory;
            existing.Entry.Command = entry.Command;
            existing.Entry.UpdateCommand = entry.UpdateCommand;
            existing.Entry.AutoStart = entry.AutoStart;
            existing.Entry.AutoRestart = entry.AutoRestart;
            existing.Entry.Environment = entry.Environment;
            existing.Entry.Shell = entry.Shell;
            existing.Entry.CustomShell = entry.CustomShell;
            existing.NotifyEntryChanged();
        }

        SelectedApp = Apps.FirstOrDefault(a => a.Entry.Id == selectedId) ?? Apps.FirstOrDefault();
        UpdateTitle();
    }

    private async void SaveConfig()
    {
        try
        {
            ConfigStore.Save(Apps.Select(a => a.Entry));
        }
        catch (Exception ex)
        {
            await ConfirmDialog.ShowAsync(this, "Could not save", $"Failed to write {ConfigPath}:\n{ex.Message}", "OK");
        }
    }

    private void UpdateTitle()
    {
        var running = Apps.Count(a => a.IsRunning);
        Title = Apps.Count == 0 ? "Corvids" : $"Corvids - {running} of {Apps.Count} running";
        App.SetTrayToolTip(Title);
        OnPropertyChanged(nameof(StatusSummary));
    }

    private async void AddApp_Click(object? sender, RoutedEventArgs e)
    {
        var entry = await new EntryDialog().ShowDialog<AppEntry?>(this);
        if (entry is null) return;

        SelectedApp = AddManaged(entry);
        SaveConfig();
    }

    private async void EditApp_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is not { } app) return;

        var edited = await new EntryDialog(app.Entry).ShowDialog<AppEntry?>(this);
        if (edited is null) return;

        app.Entry.Name = edited.Name;
        app.Entry.WorkingDirectory = edited.WorkingDirectory;
        app.Entry.Command = edited.Command;
        app.Entry.UpdateCommand = edited.UpdateCommand;
        app.Entry.AutoStart = edited.AutoStart;
        app.Entry.AutoRestart = edited.AutoRestart;
        app.Entry.Environment = edited.Environment;
        app.Entry.Shell = edited.Shell;
        app.Entry.CustomShell = edited.CustomShell;
        app.NotifyEntryChanged();
        SaveConfig();
    }

    private async void RemoveApp_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is not { } app) return;

        var message = app.IsRunning
            ? $"\"{app.Name}\" is running. Stop it and remove it from the list?"
            : $"Remove \"{app.Name}\" from the list?";
        if (!await ConfirmDialog.ShowAsync(this, "Remove app", message, "Remove")) return;

        app.Stop();
        var index = Apps.IndexOf(app);
        Apps.Remove(app);
        SelectedApp = Apps.Count == 0 ? null : Apps[Math.Min(index, Apps.Count - 1)];
        SaveConfig();
        UpdateTitle();
    }

    // Process control

    private void Start_Click(object? sender, RoutedEventArgs e) => SelectedApp?.Start();

    private void Stop_Click(object? sender, RoutedEventArgs e) => SelectedApp?.Stop();

    private async void Restart_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is { } app) await app.RestartAsync();
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is { } app) await app.RunUpdateAsync();
    }

    private void StartAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var app in Apps) app.Start();
    }

    private void StopAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var app in Apps) app.Stop();
    }

    private async void KillAllNode_Click(object? sender, RoutedEventArgs e)
    {
        var count = ProcessTools.FindNodeProcesses().Count;
        if (count == 0)
        {
            await ConfirmDialog.ShowAsync(this, "Kill all node", "No node processes are running.", "OK");
            return;
        }

        var message = $"Kill all {count} node process(es) on this machine? This includes servers started from " +
                      "your own terminals, not just the ones Corvids manages.";
        if (!await ConfirmDialog.ShowAsync(this, "Kill all node", message, "Kill all")) return;

        var killed = ProcessTools.KillAllNode();
        foreach (var app in Apps.Where(a => a.IsRunning)) app.Stop();
        await ConfirmDialog.ShowAsync(this, "Kill all node", $"Killed {killed} node process(es).", "OK");
    }

    private async void FreePort_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is { } app) await app.FreePortAndRestartAsync();
    }

    private void DismissConflict_Click(object? sender, RoutedEventArgs e) => SelectedApp?.DismissConflict();

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        var updated = await new SettingsDialog(Settings).ShowDialog<AppSettings?>(this);
        if (updated is null) return;

        var previousFile = ConfigStore.Location;
        Settings = updated;
        SettingsStore.Save(Settings);

        TimeFormat.Pattern = Settings.TimestampFormat ?? TimeFormat.Default;
        RefreshLogTimestamps();

        ConfigStore.Configure(Settings.AppsFilePath);
        if (!string.Equals(previousFile, ConfigStore.Location, StringComparison.OrdinalIgnoreCase))
        {
            // Moving to a file that does not exist yet: seed it with the current list so nothing is lost.
            if (!System.IO.File.Exists(ConfigStore.Location)) SaveConfig();
            ReloadFromDisk();
        }

        OnPropertyChanged(nameof(ConfigPath));
    }

    /// <summary>
    /// One serialized pass: a single OS listener query maps to every running app, so all sidebar dots and the
    /// status bar update together with no overlapping checks. Guarded so only one scan runs at a time.
    /// </summary>
    private async void MonitorPorts()
    {
        if (_portScanBusy) return; // never let a second scan start while one is in flight
        if (Apps.All(a => !a.IsRunning)) return;

        _portScanBusy = true;
        try
        {
            var listeners = await ProcessTools.ListeningPortsAsync();
            var byPid = listeners.ToLookup(l => l.Pid, l => l.Port);
            foreach (var app in Apps)
            {
                if (!app.IsRunning) { app.SetPorts(Array.Empty<int>()); continue; }
                var ports = app.ProcessIds.SelectMany(pid => byPid[pid]).Distinct().OrderBy(p => p).ToList();
                app.SetPorts(ports);
            }
        }
        finally
        {
            _portScanBusy = false;
        }
    }

    /// <summary>Rebinds the log so already-rendered lines pick up a changed timestamp format.</summary>
    private void RefreshLogTimestamps()
    {
        var app = SelectedApp;
        if (app is null) return;
        SelectedApp = null;
        SelectedApp = app;
    }

    private void AppList_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item?.DataContext is ManagedApp app)
        {
            SelectedApp = app;
        }
        else
        {
            e.Handled = true; // right-click on empty space: no menu
        }
    }

    private async void KillNode_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is not { } app) return;

        var related = await app.FindRelatedNodeAsync();
        if (related.Count == 0 && !app.IsRunning)
        {
            await ConfirmDialog.ShowAsync(this, "Kill node",
                $"No node processes were found in {app.WorkingDirectory} or on its port.", "OK");
            return;
        }

        var lines = related.Select(p => $"• PID {p.Pid}: {p.CommandLine}").ToList();
        if (app.IsRunning) lines.Insert(0, $"• PID {app.Pid}: this app's own process (will be stopped)");
        var message = $"Kill these {lines.Count} node process(es) for \"{app.Name}\"?\n\n" + string.Join("\n", lines);
        if (!await ConfirmDialog.ShowAsync(this, "Kill node", message, "Kill")) return;

        await app.KillRelatedNodeAsync(related);
    }

    // Log view

    private void Clear_Click(object? sender, RoutedEventArgs e) => SelectedApp?.ClearLog();

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is not { } app || Clipboard is null) return;
        await Clipboard.SetTextAsync(JoinLines(app.Lines));
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedApp is { } app) Shell.RevealFolder(app.WorkingDirectory);
    }

    private async void LogList_KeyDown(object? sender, KeyEventArgs e)
    {
        var isCopyChord = e.Key == Key.C &&
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        if (!isCopyChord || Clipboard is null) return;

        var selected = LogList.SelectedItems?.OfType<LogLine>().OrderBy(l => l.Time).ToList();
        if (selected is null || selected.Count == 0) return;

        e.Handled = true;
        await Clipboard.SetTextAsync(JoinLines(selected));
    }

    private static string JoinLines(IEnumerable<LogLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(line.TimeText).Append("  ").AppendLine(line.Text);
        return sb.ToString();
    }

    private void ScrollToEnd()
    {
        // Skip while hidden (tray) or unmeasured: ScrollIntoView then arranges an invalid rect and throws.
        if (!AutoScroll || LogList.ItemCount == 0 || !IsVisible || LogList.Bounds.Height <= 0) return;

        try
        {
            // ScrollIntoView on a line wider than the viewport also nudges the view sideways, which slowly
            // hides the timestamp column. Keep whatever horizontal position the user had.
            var horizontal = LogList.Scroll?.Offset.X ?? 0;
            LogList.ScrollIntoView(LogList.ItemCount - 1);
            if (LogList.Scroll is { } scroll && scroll.Offset.X != horizontal)
                scroll.Offset = new Avalonia.Vector(horizontal, scroll.Offset.Y);
        }
        catch
        {
            // A transient Avalonia layout hiccup during rapid updates must never crash the app.
        }
    }

    /// <summary>Scrolling up by hand pauses auto-scroll; returning to the bottom resumes it.</summary>
    private void LogList_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (LogList.Scroll is not { } scroll) return;
        if (e.ExtentDelta.Y != 0) return; // content grew or was trimmed, not a user scroll

        var atBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 1;
        if (e.OffsetDelta.Y < 0 && !atBottom) AutoScroll = false;
        else if (e.OffsetDelta.Y > 0 && atBottom) AutoScroll = true;
    }

    // Shutdown

    /// <summary>X button: with apps running, offer to keep them alive in the tray or stop everything.</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose) return;

        var running = Apps.Where(a => a.IsRunning).ToList();
        if (running.Count == 0) return;

        e.Cancel = true;
        var message = running.Count == 1
            ? $"\"{running[0].Name}\" is still running."
            : $"{running.Count} apps are still running.";

        switch (await ExitDialog.ShowAsync(this, message))
        {
            case ExitChoice.KeepInTray:
                Hide();
                break;
            case ExitChoice.StopAndExit:
                StopAllAndExit();
                break;
        }
    }

    /// <summary>Restores the window from the tray.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void StopAllAndExit()
    {
        foreach (var app in Apps.Where(a => a.IsRunning)) app.Stop();
        _forceClose = true;
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
