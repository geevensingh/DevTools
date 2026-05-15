using System;
using System.Collections.Generic;
using System.Linq;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// On-disk shape for <c>recents.json</c>. <see cref="Version"/> is bumped
/// whenever the JSON shape changes in a non-backward-compatible way; the
/// deserializer treats unknown future versions as "fall back to empty"
/// to avoid downgrade hazards.
/// </summary>
public sealed record RecentsDoc(int Version, IReadOnlyList<RecentLaunchContext> Items)
{
    public const int CurrentVersion = 1;

    public static RecentsDoc Empty { get; } = new(CurrentVersion, Array.Empty<RecentLaunchContext>());

    /// <summary>
    /// Defensive copy + integrity check for callers that build a new
    /// <see cref="RecentsDoc"/> from a mutation. Rejects null entries and
    /// snapshots the list so later in-place mutation by the caller can't
    /// corrupt the on-disk state.
    /// </summary>
    public static RecentsDoc From(IEnumerable<RecentLaunchContext> items) =>
        new(CurrentVersion, items.Where(i => i is not null).ToArray());
}
