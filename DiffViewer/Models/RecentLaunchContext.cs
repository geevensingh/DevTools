using System;

namespace DiffViewer.Models;

/// <summary>
/// One row in the recent-launch-contexts MRU. Combines the canonical
/// dedup <see cref="Identity"/> with the user's raw input
/// (<see cref="LeftDisplay"/> / <see cref="RightDisplay"/>) so the UI can
/// render exactly what was typed even though we dedup against the
/// canonical form.
///
/// <para><b>Equality</b> is record-equality (all four members), but the
/// recents service dedups <em>by Identity only</em>: re-launching with a
/// differently-cased path or different ref alias bumps the existing
/// entry rather than creating a new one. See
/// <c>RecentContextsService</c> for that policy.</para>
/// </summary>
public sealed record RecentLaunchContext(
    ContextIdentity Identity,
    DiffSide LeftDisplay,
    DiffSide RightDisplay,
    DateTimeOffset LastUsedUtc);
