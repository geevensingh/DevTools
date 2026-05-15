using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Phase-1 stub. Returns empty <see cref="Current"/>, no-ops every
/// recording call, never raises <see cref="Changed"/>. Replaced in Phase 5
/// with <c>RecentContextsService</c> (real persistence + MRU semantics).
/// </summary>
internal sealed class NullRecentContextsService : IRecentContextsService
{
    public IReadOnlyList<RecentLaunchContext> Current { get; } = Array.Empty<RecentLaunchContext>();

    public event EventHandler? Changed
    {
        add { /* no-op: stub never fires Changed */ }
        remove { /* no-op */ }
    }

    public Task RecordLaunchAsync(string repoPath, DiffSide left, DiffSide right, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string repoPath, DiffSide left, DiffSide right, CancellationToken ct = default)
        => Task.CompletedTask;
}
