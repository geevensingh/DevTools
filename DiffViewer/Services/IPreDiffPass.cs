using DiffViewer.Models;
using DiffViewer.ViewModels;

namespace DiffViewer.Services;

/// <summary>
/// Background pass that diffs every file in the change list under the
/// current toolbar options and stamps each <see cref="FileEntryViewModel"/>
/// with its <see cref="FileEntryViewModel.HasVisibleDifferences"/> result.
/// Lets the <c>(whitespace-only)</c> grey-out appear in the left pane
/// without requiring per-file clicks.
///
/// <para><b>Ordering:</b> the currently-selected file is diffed first
/// (it's the user's likely next click); the rest of the list follows in
/// FIFO order. <see cref="OnSelectionChanged"/> can re-prioritise
/// mid-pass.</para>
///
/// <para><b>Concurrency:</b> bounded by the <c>maxConcurrency</c> ctor
/// arg (default 4 - sized to keep all cores from being pinned).</para>
///
/// <para><b>Cancellation:</b> the pass cancels on
/// <see cref="OnOptionsChanged"/>, on <see cref="Start"/> being called
/// again, and on <see cref="Dispose"/>. The watcher-triggered refresh
/// path calls <see cref="Start"/> with the new entry list.</para>
///
/// <para><b>Skipped files</b> (the pass leaves
/// <see cref="FileEntryViewModel.HasVisibleDifferences"/> as <c>null</c>
/// for these): binary, LFS pointer, sparse-not-checked-out, mode-only
/// change, submodule, conflicted, and any file with either side
/// exceeding <c>largeFileThresholdBytes</c>.</para>
/// </summary>
public interface IPreDiffPass : IDisposable
{
    /// <summary>
    /// Cancel any in-flight pass and start a new one over
    /// <paramref name="entries"/> with <paramref name="selectedEntry"/> at
    /// the head of the queue.
    /// </summary>
    void Start(
        IReadOnlyList<FileEntryViewModel> entries,
        FileEntryViewModel? selectedEntry,
        DiffOptions options);

    /// <summary>
    /// Re-prioritise so <paramref name="newSelection"/> is the next entry
    /// pulled from the queue. No-op if the entry is already done.
    /// </summary>
    void OnSelectionChanged(FileEntryViewModel? newSelection);

    /// <summary>
    /// Cancel the current pass and restart with new options. Clears every
    /// stamped <see cref="FileEntryViewModel.HasVisibleDifferences"/>
    /// because the option change may flip the result.
    /// </summary>
    void OnOptionsChanged(
        IReadOnlyList<FileEntryViewModel> entries,
        FileEntryViewModel? selectedEntry,
        DiffOptions newOptions);

    /// <summary>Cancel the current pass without restarting.</summary>
    void Cancel();
}
