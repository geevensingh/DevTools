using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Cheap, cached <c>git status --porcelain</c> probe per cwd. We never block
/// the UI on this — callers fire-and-forget, then read the cached result on
/// the next refresh tick.
/// </summary>
public sealed class GitStatusProbe
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public GitStatusProbe(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromSeconds(15);

    public bool TryGetCached(string cwd, out bool isDirty, out DateTimeOffset checkedAt)
    {
        if (_cache.TryGetValue(cwd, out var e) && DateTimeOffset.UtcNow - e.At < _ttl)
        {
            isDirty = e.IsDirty;
            checkedAt = e.At;
            return true;
        }
        isDirty = false;
        checkedAt = default;
        return false;
    }

    public Task RefreshAsync(string cwd, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                var dirty = ProbeNow(cwd);
                _cache[cwd] = new CacheEntry(dirty, DateTimeOffset.UtcNow);
            }
            catch
            {
                ErrorTally.Tally("git.probe");
                _cache[cwd] = new CacheEntry(false, DateTimeOffset.UtcNow);
            }
        }, ct);

    private static bool ProbeNow(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd)) return false;

        var psi = new ProcessStartInfo("git", "status --porcelain --ignore-submodules=dirty")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        // Cap at 4 seconds — slow git status on large monorepos shouldn't block forever.
        if (!p.WaitForExit(4000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return false;
        }
        if (p.ExitCode != 0) return false;
        var output = p.StandardOutput.ReadToEnd();
        return output.Length > 0;
    }

    private readonly record struct CacheEntry(bool IsDirty, DateTimeOffset At);
}
