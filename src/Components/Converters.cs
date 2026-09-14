using Avalonia.Data.Converters;
using Avalonia.Media;
using Corvids.Models;
using Corvids.ViewModels;

namespace Corvids;

/// <summary>Value converters used from XAML via {x:Static local:Converters.Name}.</summary>
public static class Converters
{
    private static readonly IBrush Running = Brush.Parse("#4ADE80");
    private static readonly IBrush Starting = Brush.Parse("#FBBF24");
    private static readonly IBrush Stopped = Brush.Parse("#6B7280");
    private static readonly IBrush Crashed = Brush.Parse("#F87171");
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

    public static readonly IValueConverter LogKindToBrush = new FuncValueConverter<LogKind, IBrush>(kind =>
        kind switch
        {
            LogKind.Stderr => Stderr,
            LogKind.System => System,
            _ => Stdout,
        });
}
