using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Corvids;
using Corvids.Models;
using Corvids.Services;

namespace Corvids.ViewModels;

public enum AppStatus { Stopped, Starting, Running, Exited, Crashed }

/// <summary>
/// Wraps one AppEntry with a live child process, its bounded log buffer and observable status.
/// All public members are meant to be used from the UI thread; process callbacks marshal back to it.
/// </summary>
public partial class ManagedApp : INotifyPropertyChanged
{
    private const int MaxLines = 5000;
    private const int TrimBatch = 500;
    private static readonly TimeSpan PortFreeTimeout = TimeSpan.FromSeconds(8);

    private readonly ConcurrentQueue<LogLine> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private Process? _process;
    private JobObject? _job;
    private bool _stopRequested;
    private int? _exitCode;
    private AppStatus _status = AppStatus.Stopped;
    private int? _pid;
    private int? _conflictPort;
    private int? _detectedPort;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread after a batch of new lines was appended to <see cref="Lines"/>.</summary>
    public event Action? LinesAppended;

    public AppEntry Entry { get; }
    public ObservableCollection<LogLine> Lines { get; } = new();
    public DateTime? StartedAt { get; private set; }

    public ManagedApp(AppEntry entry)
    {
        Entry = entry;
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _flushTimer.Tick += (_, _) => FlushPending();
    }

    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string WorkingDirectory => Entry.WorkingDirectory;

    public AppStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Uptime));
        }
    }

    public bool IsRunning => Status is AppStatus.Starting or AppStatus.Running;

    private PortState _port = PortState.Stopped;
    private string _portText = "stopped";

    /// <summary>Port indicator, kept current by the background monitor: green (open), blue (none), gray (checking/stopped).</summary>
    public PortState Port
    {
        get => _port;
        private set { if (_port == value) return; _port = value; OnPropertyChanged(); }
    }

    /// <summary>Human-readable port text, e.g. "port 3000", "no port", "checking…", "stopped".</summary>
    public string PortText
    {
        get => _portText;
        private set { if (_portText == value) return; _portText = value; OnPropertyChanged(); }
    }

    /// <summary>Called by the monitor with the freshly scanned ports for this app.</summary>
    public void SetPorts(IReadOnlyList<int> ports)
    {
        if (!IsRunning) { Port = PortState.Stopped; PortText = "stopped"; return; }
        if (ports.Count == 0) { Port = PortState.NoPort; PortText = "no port"; return; }
        Port = PortState.Open;
        PortText = ports.Count == 1 ? $"port {ports[0]}" : "ports " + string.Join(", ", ports);
    }

    /// <summary>PIDs owned by this app: the whole job tree on Windows, otherwise the root process.</summary>
    public IReadOnlyCollection<int> ProcessIds
    {
        get
        {
            if (_job?.GetProcessIds() is { Count: > 0 } jobPids) return jobPids;
            return _process is { HasExited: false } p ? new[] { p.Id } : Array.Empty<int>();
        }
    }

    public int? Pid
    {
        get => _pid;
        private set { _pid = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    /// <summary>Port reported by an EADDRINUSE error in the log, or null. Drives the "free port" banner.</summary>
    public int? ConflictPort
    {
        get => _conflictPort;
        private set
        {
            if (_conflictPort == value) return;
            _conflictPort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConflictText));
        }
    }

    public string ConflictText => ConflictPort is { } port
        ? $"Port {port} is already in use by another process, so this app could not start."
        : "";

    public string StatusText => Status switch
    {
        AppStatus.Starting => "Starting…",
        AppStatus.Running => $"Running · PID {Pid}",
        AppStatus.Exited => $"Exited (code {_exitCode})",
        AppStatus.Crashed => _exitCode is null ? "Failed to start" : $"Crashed (code {_exitCode})",
        _ => "Stopped",
    };

    public string Uptime =>
        IsRunning && StartedAt is { } started ? FormatUptime(DateTime.Now - started) : "";

    /// <summary>Call after the underlying Entry was edited so bound labels refresh.</summary>
    public void NotifyEntryChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(WorkingDirectory));
    }

    public void RefreshUptime() => OnPropertyChanged(nameof(Uptime));

    public void Start()
    {
        if (IsRunning) return;

        _stopRequested = false;
        _exitCode = null;
        _conflictPortPending = false;
        ConflictPort = null;

        if (!Directory.Exists(Entry.WorkingDirectory))
        {
            AppendSystem($"Working directory not found: {Entry.WorkingDirectory}");
            Status = AppStatus.Crashed;
            return;
        }

        var process = new Process
        {
            StartInfo = Shell.ForCommand(Entry.Command, Entry.WorkingDirectory, Entry.Shell, Entry.CustomShell,
                out var shellLabel, Entry.ParseEnvironment()),
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Enqueue(LogKind.Stdout, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Enqueue(LogKind.Stderr, e.Data); };
        process.Exited += (_, _) => OnProcessExited(process);

        AppendSystem($"$ {Entry.Command}    (in {Entry.WorkingDirectory}, via {shellLabel})");
        Status = AppStatus.Starting;

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            AppendSystem($"Failed to start: {ex.Message}");
            Status = AppStatus.Crashed;
            process.Dispose();
            return;
        }

        // Put the whole tree in a job so Stop reaches grandchildren even if the shell re-parents them.
        _job = JobObject.TryCreate();
        _job?.Assign(process);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _process = process;
        Pid = process.Id;
        StartedAt = DateTime.Now;
        Status = AppStatus.Running;
        Port = PortState.Checking; // gray pulse until the next monitor scan resolves it
        PortText = "checking…";
        _flushTimer.Start();
    }

    public void Stop()
    {
        var process = _process;
        if (process is null) return;

        _stopRequested = true;
        _restartToken++; // cancels a pending auto-restart
        AppendSystem("Stopping…");
        try
        {
            _job?.Terminate();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppendSystem($"Stop failed: {ex.Message}");
        }
    }

    private Process? _updateProcess;

    /// <summary>True while the update command is running (disables the Update button).</summary>
    public bool IsUpdating => _updateProcess is not null;

    /// <summary>Runs the entry's update command (default "npm install") in the app folder, logging its output.</summary>
    public void RunUpdate()
    {
        if (_updateProcess is not null) { AppendSystem("Update is already running."); return; }
        if (!Directory.Exists(Entry.WorkingDirectory))
        {
            AppendSystem($"Working directory not found: {Entry.WorkingDirectory}");
            return;
        }

        var command = string.IsNullOrWhiteSpace(Entry.UpdateCommand) ? AppEntry.DefaultUpdateCommand : Entry.UpdateCommand;
        var process = new Process
        {
            StartInfo = Shell.ForCommand(command, Entry.WorkingDirectory, Entry.Shell, Entry.CustomShell,
                out var shellLabel, Entry.ParseEnvironment()),
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Enqueue(LogKind.Stdout, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Enqueue(LogKind.Stderr, e.Data); };
        process.Exited += (_, _) => OnUpdateExited(process);

        AppendSystem($"$ {command}    (update, in {Entry.WorkingDirectory}, via {shellLabel})");
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            AppendSystem($"Update failed to start: {ex.Message}");
            process.Dispose();
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _updateProcess = process;
        _flushTimer.Start(); // ensure output flushes even when the app itself is stopped
        OnPropertyChanged(nameof(IsUpdating));
    }

    private void OnUpdateExited(Process process)
    {
        int code;
        try { process.WaitForExit(2000); code = process.ExitCode; } catch { code = -1; }

        Dispatcher.UIThread.Post(() =>
        {
            AppendSystem(code == 0 ? "Update finished." : $"Update exited with code {code}.");
            FlushPending();
            if (!IsRunning) _flushTimer.Stop();
            _updateProcess = null;
            OnPropertyChanged(nameof(IsUpdating));
            process.Dispose();
        });
    }

    /// <summary>Stops, waits for the process and its port to be released, then starts again.</summary>
    public async Task RestartAsync()
    {
        if (_process is not null)
        {
            Stop();
            await WaitForExitAsync(TimeSpan.FromSeconds(10));
        }

        var port = _conflictPort ?? _detectedPort;
        if (port is { } p && !await ProcessTools.WaitForPortFreeAsync(p, PortFreeTimeout))
        {
            AppendSystem($"Port {p} is still in use; starting anyway.");
        }

        Start();
    }

    /// <summary>Kills whatever listens on the conflicting port, then restarts this app.</summary>
    public async Task FreePortAndRestartAsync()
    {
        if (ConflictPort is not { } port) return;

        var pids = await ProcessTools.FindListenersAsync(port);
        if (pids.Count == 0)
        {
            AppendSystem($"Nothing is listening on port {port} any more.");
        }

        foreach (var pid in pids)
        {
            var label = ProcessTools.DescribeProcess(pid);
            AppendSystem(ProcessTools.KillTree(pid)
                ? $"Killed {label} that was holding port {port}."
                : $"Could not kill {label} on port {port}.");
        }

        ConflictPort = null;
        await RestartAsync();
    }

    public void DismissConflict() => ConflictPort = null;

    /// <summary>Node processes that belong to this app: in its folder, or holding its port. Own PID excluded.</summary>
    public async Task<List<ProcessTools.NodeProcess>> FindRelatedNodeAsync()
    {
        var found = new Dictionary<int, ProcessTools.NodeProcess>();

        foreach (var process in await ProcessTools.FindNodeProcessesInAsync(Entry.WorkingDirectory))
            found[process.Pid] = process;

        var port = _conflictPort ?? _detectedPort;
        if (port is { } p)
        {
            foreach (var pid in await ProcessTools.FindListenersAsync(p))
                found.TryAdd(pid,
                    new ProcessTools.NodeProcess(pid, $"{ProcessTools.DescribeProcess(pid)} on port {p}", null));
        }

        if (Pid is { } own) found.Remove(own);
        return found.Values.OrderBy(v => v.Pid).ToList();
    }

    /// <summary>Stops this app if running, then kills every related node process. Returns how many were killed.</summary>
    public async Task<int> KillRelatedNodeAsync(IReadOnlyList<ProcessTools.NodeProcess> related)
    {
        if (_process is not null)
        {
            Stop();
            await WaitForExitAsync(TimeSpan.FromSeconds(10));
        }

        var killed = 0;
        foreach (var process in related)
        {
            var label = ProcessTools.DescribeProcess(process.Pid);
            if (ProcessTools.KillTree(process.Pid))
            {
                killed++;
                AppendSystem($"Killed {label}: {Shorten(process.CommandLine)}");
            }
            else
            {
                AppendSystem($"Could not kill {label} (already gone or access denied).");
            }
        }

        if (related.Count == 0) AppendSystem("No other node processes found for this app.");
        ConflictPort = null;
        return killed;
    }

    private static string Shorten(string text) => text.Length <= 120 ? text : text[..117] + "...";

    public void ClearLog() => Lines.Clear();

    private async Task WaitForExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.Now + timeout;
        while (_process is not null && DateTime.Now < deadline) await Task.Delay(50);
    }

    private void OnProcessExited(Process process)
    {
        int code;
        try
        {
            // Bounded: an orphaned grandchild can keep our stdout pipe open forever otherwise.
            process.WaitForExit(2000);
            code = process.ExitCode;
        }
        catch
        {
            code = -1;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_process, process)) return;

            _process = null;
            _exitCode = code;
            Pid = null;
            Port = PortState.Stopped;
            PortText = "stopped";
            Status = _stopRequested ? AppStatus.Stopped : code == 0 ? AppStatus.Exited : AppStatus.Crashed;
            AppendSystem(_stopRequested ? "Stopped." : $"Process exited with code {code}.");
            FlushPending();
            _flushTimer.Stop();
            _job?.Dispose();
            _job = null;
            process.Dispose();

            if (!_stopRequested && Entry.AutoRestart) ScheduleRestart();
        });
    }

    // Auto-restart (pm2-style): 5 s delay, give up after 10 unexpected exits within 10 minutes.

    private const int MaxRestarts = 10;
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);
    private readonly Queue<DateTime> _recentExits = new();
    private int _restartToken;

    private async void ScheduleRestart()
    {
        var now = DateTime.Now;
        _recentExits.Enqueue(now);
        while (_recentExits.Count > 0 && now - _recentExits.Peek() > RestartWindow) _recentExits.Dequeue();

        if (_recentExits.Count > MaxRestarts)
        {
            AppendSystem($"Crashed {MaxRestarts} times in {RestartWindow.TotalMinutes:0} minutes; not restarting again. " +
                         "Press Start to retry.");
            _recentExits.Clear();
            return;
        }

        var token = ++_restartToken;
        AppendSystem($"Auto-restart in {RestartDelay.TotalSeconds:0} s (attempt {_recentExits.Count} of {MaxRestarts})…");
        await Task.Delay(RestartDelay);
        if (token != _restartToken || IsRunning) return; // user started or stopped it meanwhile

        Start();
    }

    private void Enqueue(LogKind kind, string text)
    {
        // Progress bars redraw with '\r'; keep only the final segment so the line reads correctly.
        var carriage = text.LastIndexOf('\r');
        if (carriage >= 0) text = text[(carriage + 1)..];
        _pending.Enqueue(new LogLine(DateTime.Now, kind, AnsiPattern().Replace(text, "")));
    }

    private void AppendSystem(string text)
    {
        _pending.Enqueue(new LogLine(DateTime.Now, LogKind.System, text));
        FlushPending();
    }

    private void FlushPending()
    {
        if (_pending.IsEmpty) return;

        while (_pending.TryDequeue(out var line))
        {
            Lines.Add(line);
            if (line.Kind != LogKind.System) InspectForPorts(line.Text);
        }

        if (Lines.Count > MaxLines + TrimBatch)
        {
            var excess = Lines.Count - MaxLines;
            for (var i = 0; i < excess; i++) Lines.RemoveAt(0);
        }

        LinesAppended?.Invoke();
    }

    /// <summary>Learns the port the app listens on, and spots "address already in use" failures.</summary>
    private void InspectForPorts(string text)
    {
        if (text.Contains("EADDRINUSE", StringComparison.Ordinal))
        {
            var match = PortAfterColon().Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var busy)) ConflictPort = busy;
            else _conflictPortPending = true;
            return;
        }

        // Some stacks print "EADDRINUSE" on one line and "port: 3003" a few lines later.
        if (_conflictPortPending && PortField().Match(text) is { Success: true } field &&
            int.TryParse(field.Groups[1].Value, out var late))
        {
            ConflictPort = late;
            _conflictPortPending = false;
            return;
        }

        if (text.Contains("listen", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("localhost:", StringComparison.OrdinalIgnoreCase))
        {
            var match = PortAfterColon().Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port)) _detectedPort = port;
        }
    }

    private bool _conflictPortPending;

    private static string FormatUptime(TimeSpan span) =>
        span.TotalHours >= 1 ? $"up {(int)span.TotalHours}h {span.Minutes:00}m"
        : span.TotalMinutes >= 1 ? $"up {span.Minutes}m {span.Seconds:00}s"
        : $"up {span.Seconds}s";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\))")]
    private static partial Regex AnsiPattern();

    /// <summary>Last ":port" in a line, e.g. ":::3003", "localhost:3000", "http://127.0.0.1:5173/".</summary>
    [GeneratedRegex(@":(\d{2,5})(?!\d)(?:/|\s|$|[,)\]'""])")]
    private static partial Regex PortAfterColon();

    [GeneratedRegex(@"\bport\b\s*[:=]?\s*'?(\d{2,5})")]
    private static partial Regex PortField();
}
