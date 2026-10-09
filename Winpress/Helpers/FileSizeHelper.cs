namespace Winpress.Helpers;

/// <summary>
/// Converts raw byte counts to human-readable strings, matching the style used
/// by Windows Explorer (1 decimal place, 1024-based units).
/// </summary>
public static class FileSizeHelper
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0)  return string.Empty;
        if (bytes == 0) return "0 B";

        var unit  = 0;
        var value = (double)bytes;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:F1} {Units[unit]}";
    }

    /// <summary>Formats transfer speed (bytes/second) to a readable throughput string.</summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "—";
        return $"{Format((long)bytesPerSecond)}/s";
    }

    /// <summary>Formats seconds into a "X min Y sec" remaining-time string.</summary>
    public static string FormatTimeRemaining(double seconds)
    {
        if (seconds <= 0)  return "—";
        if (seconds < 60)  return $"{(int)seconds}s remaining";
        var min = (int)(seconds / 60);
        var sec = (int)(seconds % 60);
        return $"{min}m {sec}s remaining";
    }
}
