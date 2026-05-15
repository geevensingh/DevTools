using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// In-memory MRU store of recent launch contexts. App-level singleton —
/// survives in-place context switches. The real implementation persists to
/// <c>%APPDATA%\DiffViewer\recents.json</c> via a small <c>RecentsStore</c>
/// helper using <see cref="System.IO.FileShare.None"/> for cross-process
/// coordination.
///
/// <para>Phase 1 ships with a <c>NullRecentContextsService</c> stub —
/// returns empty <see cref="Current"/>, no-ops <see cref="RecordLaunchAsync"/>
/// — so the rest of the composition graph can be wired without persistence
/// in place. The real service lands in Phase 5.</para>
/// </summary>
public interface IRecentContextsService
{
    /// <summary>MRU-ordered snapshot of recent contexts. Empty until <see cref="RecordLaunchAsync"/> is called.</summary>
    IReadOnlyList<RecentLaunchContext> Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes (record / remove).</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Record a successful launch into the MRU. Dedups by
    /// <paramref name="identity"/>, bumps the entry's
    /// <see cref="RecentLaunchContext.LastUsedUtc"/> and moves it to the
    /// front, caps total entries at 10. The <paramref name="leftDisplay"/>
    /// / <paramref name="rightDisplay"/> arguments are the user's raw
    /// input and are preserved verbatim for the dropdown render — they
    /// may differ in casing or alias from the identity's sides.
    /// </summary>
    Task RecordLaunchAsync(
        ContextIdentity identity,
        DiffSide leftDisplay,
        DiffSide rightDisplay,
        CancellationToken ct = default);

    /// <summary>
    /// Remove an entry from the MRU. Used by the failed-switch flow when
    /// a recent's repo no longer resolves.
    /// </summary>
    Task RemoveAsync(
        ContextIdentity identity,
        CancellationToken ct = default);
}
