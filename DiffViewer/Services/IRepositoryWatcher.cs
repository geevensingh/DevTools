using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Watches a working-tree-bearing repository for external mutations and
/// raises a single debounced <see cref="Changed"/> event per quiescent
/// window. Always inactive for commit-vs-commit comparisons (the consumer
/// shouldn't even construct one in that case). Supports nested
/// <see cref="Suspend"/> for coordination with <c>git.exe</c> write
/// operations - the event fires once after the outermost resume if any
/// raw events arrived while suspended.
/// </summary>
public interface IRepositoryWatcher : IDisposable
{
    /// <summary>Raised on a background thread once per debounce window.</summary>
    event EventHandler<RepositoryChangedEventArgs>? Changed;

    /// <summary>Begin watching. Idempotent.</summary>
    void Start();

    /// <summary>
    /// Suspend the debounced fire until the returned token is disposed.
    /// Multiple concurrent suspends compose: the watcher only resumes when
    /// the last token is disposed. If any raw events arrived during the
    /// suspension, one <see cref="Changed"/> fires immediately on resume
    /// with the accumulated kind bitmask.
    /// </summary>
    IDisposable Suspend();
}
