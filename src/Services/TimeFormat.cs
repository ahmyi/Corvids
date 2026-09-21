using System.Globalization;
using System.Text;

namespace Corvids.Services;

/// <summary>
/// Formats log timestamps from a user-supplied strftime-style pattern (e.g. "%y-%m-%d %H:%M:%S").
/// Unknown "%x" tokens are left as-is, so a bad pattern never throws.
/// </summary>
public static class TimeFormat
{
    public const string Default = "%y-%m-%d %H:%M:%S";

    /// <summary>The active pattern, applied to every log line. Set at startup and when Settings change.</summary>
    public static string Pattern { get; set; } = Default;

    /// <summary>Key -> short description, shown as the "Key for Date Time Input" legend in Settings.</summary>
    public static readonly (string Token, string Meaning)[] Legend =
    {
        ("%Y", "year 2026"),
        ("%y", "year 26"),
        ("%m", "month"),
        ("%d", "day"),
        ("%H", "hour 24h"),
        ("%I", "hour 12h"),
        ("%M", "minute"),
        ("%S", "second"),
        ("%f", "millis"),
        ("%p", "AM/PM"),
    };

    public static string Format(DateTime time) => Format(time, Pattern);

    public static string Format(DateTime time, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) pattern = Default;

        var sb = new StringBuilder(pattern.Length + 8);
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            // "%" followed by a known token is substituted; a lone "%" or an unknown one is a literal percent
            // (the following char is left to be processed normally on the next pass).
            var value = i + 1 < pattern.Length ? MapToken(pattern[i + 1], time) : null;
            if (value is null)
            {
                sb.Append('%');
            }
            else
            {
                sb.Append(value);
                i++;
            }
        }

        return sb.ToString();
    }

    private static string? MapToken(char token, DateTime time) => token switch
    {
        'Y' => time.ToString("yyyy", CultureInfo.InvariantCulture),
        'y' => time.ToString("yy", CultureInfo.InvariantCulture),
        'm' => time.ToString("MM", CultureInfo.InvariantCulture),
        'd' => time.ToString("dd", CultureInfo.InvariantCulture),
        'H' => time.ToString("HH", CultureInfo.InvariantCulture),
        'I' => time.ToString("hh", CultureInfo.InvariantCulture),
        'M' => time.ToString("mm", CultureInfo.InvariantCulture),
        'S' => time.ToString("ss", CultureInfo.InvariantCulture),
        'f' => time.ToString("fff", CultureInfo.InvariantCulture),
        'p' => time.ToString("tt", CultureInfo.InvariantCulture),
        _ => null,
    };
}
