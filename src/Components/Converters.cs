using Avalonia.Data.Converters;
using Avalonia.Media;
using Corvids.Models;
using Corvids.ViewModels;

namespace Corvids;

/// <summary>Port indicator state for the status bar and sidebar dots.</summary>
public enum PortState
{
    Checking, // gray, pulsing: a scan has not resolved this app yet
    Open,     // green: at least one listening port
    NoPort,   // blue: running but not listening on any port (not an error)
    Stopped,  // gray, static: app is not running
}

/// <summary>Value converters used from XAML via {x:Static local:Converters.Name}.</summary>
public static class Converters
{
    private static readonly IBrush Running = Brush.Parse("#4ADE80");
    private static readonly IBrush Starting = Brush.Parse("#FBBF24");
    private static readonly IBrush Stopped = Brush.Parse("#6B7280");
    private static readonly IBrush Crashed = Brush.Parse("#F87171");
    private static readonly IBrush NoPort = Brush.Parse("#3B82F6"); // blue: running but no port (not an error)
    private static readonly IBrush Stdout = Brush.Parse("#D4D4D4");
    private static readonly IBrush Stderr = Brush.Parse("#F2A0A0");
    private static readonly IBrush System = Brush.Parse("#A78BFA");

    public static readonly IValueConverter StatusToBrush = new FuncValueConverter<AppStatus, IBrush>(status =>
        status switch
        {
            AppStatus.Running => Running,
            AppStatus.Starting => Starting,
            AppStatus.Crashed => Crashed,
            AppStatus.Exited => Stopped,
            _ => Stopped,
        });

    /// <summary>Green when a port is open, blue when running with none, gray while checking or stopped.</summary>
    public static readonly IValueConverter PortStateToBrush = new FuncValueConverter<PortState, IBrush>(state =>
        state switch
        {
            PortState.Open => Running,
            PortState.NoPort => NoPort,
            _ => Stopped,
        });

    /// <summary>True only while a scan has not yet resolved this app, to drive the pulsing dot.</summary>
    public static readonly IValueConverter PortStateToChecking =
        new FuncValueConverter<PortState, bool>(state => state == PortState.Checking);

    public static readonly IValueConverter LogKindToBrush = new FuncValueConverter<LogKind, IBrush>(kind =>
        kind switch
        {
            LogKind.Stderr => Stderr,
            LogKind.System => System,
            _ => Stdout,
        });
}
