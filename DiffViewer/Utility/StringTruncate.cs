namespace DiffViewer.Utility;

/// <summary>
/// String-shaping helpers for UI labels.
/// </summary>
public static class StringTruncate
{
    /// <summary>
    /// Truncate <paramref name="value"/> to at most <paramref name="maxLength"/>
    /// characters by removing the middle and inserting an ellipsis (…).
    /// Returns <paramref name="value"/> unchanged when it already fits.
    /// </summary>
    /// <example>
    /// MidTruncate("feature/my-very-long-branch-name", 20) →
    /// "feature/m…ranch-name"
    /// </example>
    public static string MidTruncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (maxLength < 3) return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        if (value.Length <= maxLength) return value;

        // Reserve 1 char for the ellipsis; split the remaining budget between
        // head and tail, biasing the head by one when the budget is odd.
        int budget = maxLength - 1;
        int head = (budget + 1) / 2;
        int tail = budget - head;
        return string.Concat(value.AsSpan(0, head), "…", value.AsSpan(value.Length - tail, tail));
    }
}
