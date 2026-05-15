using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Phase 1 stub: an <see cref="IRecentContextsService"/> that has no
/// state and no IO. <see cref="Current"/> is always empty;
/// <see cref="RecordLaunchAsync"/> and <see cref="RemoveAsync"/> are
/// no-ops. Used during the architectural-scaffolding phases so the
/// coordinator graph can be wired without dragging in persistence yet.
/// </summary>
public sealed class NullRecentContextsService : IRecentContextsService
{
    public IReadOnlyList<RecentLaunchContext> Current { get; } = Array.Empty<RecentLaunchContext>();

#pragma warning disable CS0067 // Event never used — by design (stub)
    public event EventHandler? Changed;
#pragma warning restore CS0067

    public Task RecordLaunchAsync(
        ContextIdentity identity,
        DiffSide leftDisplay,
        DiffSide rightDisplay,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(
        ContextIdentity identity,
        CancellationToken ct = default) => Task.CompletedTask;
}
