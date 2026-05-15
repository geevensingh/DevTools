using System;

namespace DiffViewer.Utility;

/// <summary>
/// Formats a <see cref="DateTimeOffset"/> as a short, human-friendly
/// relative-time label. Used by the recents dropdown's secondary line.
///
/// <para>Buckets:</para>
/// <list type="bullet">
///   <item>&lt; 60s → <c>"just now"</c></item>
///   <item>&lt; 60m → <c>"5m ago"</c></item>
///   <item>&lt; 24h → <c>"3h ago"</c></item>
///   <item>&lt; 7d  → <c>"Tue"</c> (day-of-week)</item>
///   <item>same year → <c>"May 14"</c></item>
///   <item>otherwise → <c>"2024-05-14"</c></item>
/// </list>
/// </summary>
public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset value, DateTimeOffset? now = null)
    {
        var n = now ?? DateTimeOffset.UtcNow;
        var delta = n - value;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;

        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24)   return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7)     return value.ToLocalTime().ToString("ddd", System.Globalization.CultureInfo.CurrentCulture);

        var local = value.ToLocalTime();
        var nLocal = n.ToLocalTime();
        if (local.Year == nLocal.Year)
            return local.ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture);

        return local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
