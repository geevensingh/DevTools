using System.Collections.Concurrent;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Central counter for swallowed exceptions across the data layer. Each call
/// site that <c>catch</c>es and continues calls <see cref="Tally"/> with a
/// short stable key (e.g. <c>"tailer.pump"</c>); the counts are flushed
/// periodically to the diagnostic log so silent failures become visible
/// without changing exception-handling semantics.
/// </summary>
public static class ErrorTally
{
    private static readonly ConcurrentDictionary<string, long> s_counts = new();

    public static void Tally(string key) => s_counts.AddOrUpdate(key, 1, (_, v) => v + 1);

    /// <summary>Atomically read-and-clear the counters; emit each via <paramref name="sink"/>.</summary>
    public static void Flush(Action<string, long> sink)
    {
        foreach (var key in s_counts.Keys.ToArray())
        {
            if (s_counts.TryRemove(key, out var n) && n > 0)
            {
                sink(key, n);
            }
        }
    }
}
