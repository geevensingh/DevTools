using System;

namespace DiffViewer.Models;

/// <summary>
/// One entry in the recent-launch-contexts MRU. Carries the user's raw
/// input for both <see cref="DiffSide"/>s (commit-ish reference strings
/// re-resolve as the repo evolves; working-tree round-trips trivially) so
/// the dropdown renders exactly what the user typed.
///
/// <para>Phase 1 shape — Phase 3 introduces <c>ContextIdentity</c> as a
/// canonical-form value type and dedup key, at which point this record
/// will gain an <c>Identity</c> alongside the display sides.</para>
/// </summary>
public sealed record RecentLaunchContext(
    string RepoPath,
    DiffSide Left,
    DiffSide Right,
    DateTimeOffset LastUsedUtc);
