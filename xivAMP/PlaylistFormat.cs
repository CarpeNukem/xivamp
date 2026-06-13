using System.Globalization;

namespace xivAMP;

/// <summary>
/// Shared helpers for playlist entry identity and duration parsing/formatting.
/// Single source of truth — do not duplicate these in windows or services.
/// </summary>
public static class PlaylistFormat
{
    public static string EntryKey(string optionGroup, string optionName)
        => $"{optionGroup}\u001F{optionName}";

    public static bool IsEntryIdentity(PlaylistEntry entry, string optionGroup, string optionName)
        => string.Equals(entry.OptionGroup, optionGroup, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.OptionName, optionName, StringComparison.OrdinalIgnoreCase);

    public static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Parse plain seconds, m:ss, mm:ss, or h:mm:ss.</summary>
    public static bool TryParseDuration(string value, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return seconds > 0;

        var parts = value.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs))
        {
            seconds = minutes * 60 + secs;
            return seconds > 0;
        }

        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out secs))
        {
            seconds = hours * 3600 + minutes * 60 + secs;
            return seconds > 0;
        }

        return false;
    }
}
