using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Cheap, cached probe per cwd of <c>git status --porcelain --branch</c>.
/// Returns both the live branch name and the dirty flag in a single call —
/// critical because Copilot CLI's <c>workspace.yaml</c> writes the branch
/// only at session start and never updates it when the user (or agent)
/// checks out a different branch later. We never block the UI on this:
/// callers fire-and-forget, then read the cached result on the next
/// refresh tick.
/// </summary>
public sealed class GitStatusProbe
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public GitStatusProbe(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromSeconds(15);

    public bool TryGetCached(string cwd, out bool isDirty, out string? branch, out DateTimeOffset checkedAt)
    {
        if (_cache.TryGetValue(cwd, out var e) && DateTimeOffset.UtcNow - e.At < _ttl)
        {
            isDirty = e.IsDirty;
            branch = e.Branch;
            checkedAt = e.At;
            return true;
        }
        isDirty = false;
        branch = null;
        checkedAt = default;
        return false;
    }

    public Task RefreshAsync(string cwd, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                var (dirty, branch) = ProbeNow(cwd);
                _cache[cwd] = new CacheEntry(dirty, branch, DateTimeOffset.UtcNow);
            }
            catch
            {
                ErrorTally.Tally("git.probe");
                _cache[cwd] = new CacheEntry(false, null, DateTimeOffset.UtcNow);
            }
        }, ct);

    private static (bool dirty, string? branch) ProbeNow(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd)) return (false, null);

        // --branch adds a leading "## <branch>...<upstream>" line that we
        // parse for the live branch name. The rest of stdout is the regular
        // porcelain dirty list, which is empty iff the tree is clean.
        var psi = new ProcessStartInfo("git", "status --porcelain --branch --ignore-submodules=dirty")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return (false, null);
        if (!p.WaitForExit(4000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return (false, null);
        }
        if (p.ExitCode != 0) return (false, null);

        var output = p.StandardOutput.ReadToEnd();
        if (string.IsNullOrEmpty(output)) return (false, null);

        // Format:
        //   ## <branch-info>      <- always present with --branch
        //   <other lines, one per dirty/untracked entry>
        string? branch = null;
        bool dirty = false;
        using var reader = new StringReader(output);
        string? line;
        bool first = true;
        while ((line = reader.ReadLine()) is not null)
        {
            if (first)
            {
                first = false;
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    branch = ParseBranch(line.Substring(3));
                }
                continue;
            }
            // Any non-header line means we have at least one dirty/untracked entry.
            if (line.Length > 0) dirty = true;
        }
        return (dirty, branch);
    }

    /// <summary>
    /// Parses the <c>--branch</c> header line content (everything after "## ").
    /// Common shapes:
    ///   <c>main</c>                                — fresh checkout, no upstream
    ///   <c>main...origin/main</c>                  — tracking, in sync
    ///   <c>main...origin/main [ahead 2]</c>        — divergence info
    ///   <c>HEAD (no branch)</c>                    — detached HEAD
    /// </summary>
    private static string? ParseBranch(string headerBody)
    {
        if (string.IsNullOrWhiteSpace(headerBody)) return null;
        // Detached: literal "HEAD (no branch)"
        if (headerBody.StartsWith("HEAD ", StringComparison.Ordinal)) return "(detached)";
        // Strip the upstream and divergence info if present.
        var dotIdx = headerBody.IndexOf("...", StringComparison.Ordinal);
        var name = dotIdx >= 0 ? headerBody.Substring(0, dotIdx) : headerBody;
        var spaceIdx = name.IndexOf(' ');
        if (spaceIdx >= 0) name = name.Substring(0, spaceIdx);
        return name.Length > 0 ? name : null;
    }

    private readonly record struct CacheEntry(bool IsDirty, string? Branch, DateTimeOffset At);
}

