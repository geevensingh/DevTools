using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Disk-backed implementation of <see cref="IRecentContextsService"/>.
/// Replaces the Phase-1 <see cref="NullRecentContextsService"/> stub.
///
/// <para><b>State model</b>: an in-memory <see cref="Current"/> snapshot
/// (always MRU-sorted, capped at <see cref="MaxEntries"/>) plus a
/// kernel-coordinated read-modify-write through <see cref="RecentsStore"/>.
/// Each public mutation re-reads disk inside the FileShare.None critical
/// section and merges, so contributions from a concurrently-running
/// DiffViewer process are preserved (no lost-update).</para>
///
/// <para><b>Thread-safety</b>: a single <see cref="SemaphoreSlim"/>
/// serialises this instance's own operations against itself.
/// Cross-process safety is delegated to <see cref="RecentsStore"/>'s
/// FileShare.None lock.</para>
///
/// <para><b>Dedup rule</b>: <see cref="ContextIdentityFactory.RepoPathsEqual"/>
/// for the path (case-insensitive on Windows file system) plus record
/// equality on the two <see cref="DiffSide"/> values (case-sensitive on
/// CommitIsh refs — <c>HEAD</c> ≠ <c>head</c>; matches the rule baked
/// into <see cref="ContextIdentity"/>).</para>
/// </summary>
public sealed class RecentContextsService : IRecentContextsService
{
    public const int MaxEntries = 10;

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<RecentLaunchContext> _current = Array.Empty<RecentLaunchContext>();

    /// <summary>Default file path under <c>%APPDATA%\DiffViewer\recents.json</c> (mirrors <see cref="SettingsService.DefaultFilePath"/>).</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DiffViewer",
        "recents.json");

    public IReadOnlyList<RecentLaunchContext> Current => _current;

    public event EventHandler? Changed;

    public RecentContextsService() : this(DefaultFilePath) { }

    public RecentContextsService(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>
    /// Hydrate <see cref="Current"/> from disk. Call once at startup
    /// before the dropdown is shown. Safe to call multiple times — each
    /// call re-reads disk and replaces the in-memory snapshot.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await RecentsStore.LoadAsync(_filePath, ct).ConfigureAwait(false);
            ReplaceSnapshot(SortAndCap(doc.Items));
        }
        finally { _gate.Release(); }
    }

    public async Task RecordLaunchAsync(
        ContextIdentity identity,
        DiffSide leftDisplay,
        DiffSide rightDisplay,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(leftDisplay);
        ArgumentNullException.ThrowIfNull(rightDisplay);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fresh = new RecentLaunchContext(identity, leftDisplay, rightDisplay, DateTimeOffset.UtcNow);

            var doc = await RecentsStore.ReadAndMutateAsync(
                _filePath,
                current => RecentsDoc.From(MergeAndCap(current.Items, fresh)),
                ct).ConfigureAwait(false);

            ReplaceSnapshot(SortAndCap(doc.Items));
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(ContextIdentity identity, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await RecentsStore.ReadAndMutateAsync(
                _filePath,
                current => RecentsDoc.From(current.Items.Where(i => !IsSameIdentity(i.Identity, identity))),
                ct).ConfigureAwait(false);

            ReplaceSnapshot(SortAndCap(doc.Items));
        }
        finally { _gate.Release(); }
    }

    private static IEnumerable<RecentLaunchContext> MergeAndCap(
        IReadOnlyList<RecentLaunchContext> existing,
        RecentLaunchContext fresh)
    {
        // Drop any entry with the same identity, then prepend the fresh
        // one (whose LastUsedUtc is now). MRU sort + cap happens in
        // ReplaceSnapshot; this method's job is to produce the merged
        // unsorted list so the on-disk state always contains the union
        // (minus dupes).
        var merged = existing
            .Where(i => !IsSameIdentity(i.Identity, fresh.Identity))
            .Append(fresh);

        return SortAndCap(merged);
    }

    private static IReadOnlyList<RecentLaunchContext> SortAndCap(IEnumerable<RecentLaunchContext> items)
    {
        var sorted = items
            .OrderByDescending(i => i.LastUsedUtc)
            .Take(MaxEntries)
            .ToArray();
        return sorted;
    }

    private static bool IsSameIdentity(ContextIdentity a, ContextIdentity b)
    {
        // Path equality is case-insensitive on Windows; DiffSide equality
        // is record-based (case-sensitive on CommitIsh refs by design —
        // see ContextIdentity docstring).
        return ContextIdentityFactory.RepoPathsEqual(a.CanonicalRepoPath, b.CanonicalRepoPath)
            && Equals(a.Left, b.Left)
            && Equals(a.Right, b.Right);
    }

    private void ReplaceSnapshot(IReadOnlyList<RecentLaunchContext> snapshot)
    {
        _current = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
