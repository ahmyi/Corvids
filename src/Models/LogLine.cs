using Corvids.Services;

namespace Corvids.Models;

public enum LogKind { Stdout, Stderr, System }

public record LogLine(DateTime Time, LogKind Kind, string Text)
{
    public string TimeText => TimeFormat.Format(Time);
}
