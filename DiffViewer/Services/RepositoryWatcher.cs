using System.IO;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Watches the working directory + the resolved <c>.git</c> directory of
/// a repository for external mutations, applies path-based filters, and
/// delegates debouncing to <see cref="RepositoryEventDebouncer"/>.
///
/// <para><b>Why two separate watchers, not one rooted at a common
/// ancestor:</b> linked worktrees (<c>git worktree add</c>) put the real
/// <c>HEAD</c>/<c>index</c> outside the worktree directory; a single
/// watcher rooted at the worktree would never see those events. The
/// two-watcher pattern also lets us avoid subscribing to
/// <c>.git\objects\</c> traffic (the bulk of in-<c>.git</c> event volume
/// during <c>git gc</c> / <c>git fetch</c>).</para>
///
/// <para><b>Path filter for <c>.git</c>:</b> only events for files
/// directly inside <c>.git</c> are kept (no recursion into
/// <c>.git\refs</c>, <c>.git\objects</c>, etc.), and only the well-known
/// state-tracking files survive the filter
/// (<see cref="IsTrackedGitDirFile"/>).</para>
///
/// <para><b>Path filter for the working tree:</b> every raw FSW path is
/// normalised to a forward-slash repo-relative path and passed to the
/// supplied ignore predicate; ignored paths are dropped.</para>
/// </summary>
public sealed class RepositoryWatcher : IRepositoryWatcher
{
    /// <summary>500 ms - the spec default. Long enough to collapse
    /// editor-save bursts, short enough to feel instant.</summary>
    public static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromMilliseconds(500);

    private const int FswMaxBufferSize = 64 * 1024; // OS max

    private readonly string _workingDirectory;
    private readonly string _gitDirectory;
    private readonly Func<string, bool> _isPathIgnored;
    private readonly RepositoryEventDebouncer _debouncer;
    private readonly object _lock = new();

    private FileSystemWatcher? _workingTreeWatcher;
    private FileSystemWatcher? _gitDirWatcher;
    private bool _started;
    private bool _disposed;

    public event EventHandler<RepositoryChangedEventArgs>? Changed;

    public RepositoryWatcher(
        string workingDirectory,
        string gitDirectory,
        Func<string, bool> isPathIgnored,
        TimeSpan? debounceInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);
        ArgumentNullException.ThrowIfNull(isPathIgnored);

        _workingDirectory = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar);
        _gitDirectory = Path.GetFullPath(gitDirectory).TrimEnd(Path.DirectorySeparatorChar);
        _isPathIgnored = isPathIgnored;
        _debouncer = new RepositoryEventDebouncer(
            debounceInterval ?? DefaultDebounceInterval,
            FireChanged);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RepositoryWatcher));
            if (_started) return;
            _started = true;
            CreateWatchersLocked();
        }
    }

    public IDisposable Suspend() => _debouncer.Suspend();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeWatchersLocked();
        }
        _debouncer.Dispose();
    }

    private void CreateWatchersLocked()
    {
        // Working-tree watcher: subdirectories on, every change kind.
        _workingTreeWatcher = new FileSystemWatcher(_workingDirectory)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = FswMaxBufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.CreationTime,
        };
        _workingTreeWatcher.Changed += OnWorkingTreeRawEvent;
        _workingTreeWatcher.Created += OnWorkingTreeRawEvent;
        _workingTreeWatcher.Deleted += OnWorkingTreeRawEvent;
        _workingTreeWatcher.Renamed += OnWorkingTreeRawEvent;
        _workingTreeWatcher.Error += OnFswError;
        _workingTreeWatcher.EnableRaisingEvents = true;

        // .git dir watcher: subdirectories OFF (we only care about files
        // directly in .git\), and we filter the names in the handler.
        _gitDirWatcher = new FileSystemWatcher(_gitDirectory)
        {
            IncludeSubdirectories = false,
            InternalBufferSize = FswMaxBufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                           NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _gitDirWatcher.Changed += OnGitDirRawEvent;
        _gitDirWatcher.Created += OnGitDirRawEvent;
        _gitDirWatcher.Deleted += OnGitDirRawEvent;
        _gitDirWatcher.Renamed += OnGitDirRawEvent;
        _gitDirWatcher.Error += OnFswError;
        _gitDirWatcher.EnableRaisingEvents = true;
    }

    private void DisposeWatchersLocked()
    {
        if (_workingTreeWatcher is not null)
        {
            _workingTreeWatcher.EnableRaisingEvents = false;
            _workingTreeWatcher.Changed -= OnWorkingTreeRawEvent;
            _workingTreeWatcher.Created -= OnWorkingTreeRawEvent;
            _workingTreeWatcher.Deleted -= OnWorkingTreeRawEvent;
            _workingTreeWatcher.Renamed -= OnWorkingTreeRawEvent;
            _workingTreeWatcher.Error -= OnFswError;
            _workingTreeWatcher.Dispose();
            _workingTreeWatcher = null;
        }
        if (_gitDirWatcher is not null)
        {
            _gitDirWatcher.EnableRaisingEvents = false;
            _gitDirWatcher.Changed -= OnGitDirRawEvent;
            _gitDirWatcher.Created -= OnGitDirRawEvent;
            _gitDirWatcher.Deleted -= OnGitDirRawEvent;
            _gitDirWatcher.Renamed -= OnGitDirRawEvent;
            _gitDirWatcher.Error -= OnFswError;
            _gitDirWatcher.Dispose();
            _gitDirWatcher = null;
        }
    }

    private void OnWorkingTreeRawEvent(object sender, FileSystemEventArgs e)
    {
        // Skip events that happen to land inside the .git directory - those
        // are owned by the other watcher and would double-fire.
        if (IsInsideGitDir(e.FullPath)) return;

        string relPath = ToRepoRelativeForwardSlash(e.FullPath);
        if (string.IsNullOrEmpty(relPath)) return;
        if (_isPathIgnored(relPath)) return;

        _debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
    }

    private void OnGitDirRawEvent(object sender, FileSystemEventArgs e)
    {
        string fileName = Path.GetFileName(e.Name) ?? string.Empty;
        if (!IsTrackedGitDirFile(fileName)) return;

        _debouncer.OnRawEvent(RepositoryChangeKind.GitDir);
    }

    private void OnFswError(object sender, ErrorEventArgs e)
    {
        // Reconstruct the watchers - the OS buffer overflowed and we may
        // have missed events. The consumer of BufferOverflow does the full
        // re-enumeration; we just rebuild the FSW infrastructure so we
        // don't loop forever on a still-attached, still-overflowing watcher.
        lock (_lock)
        {
            if (_disposed) return;
            DisposeWatchersLocked();
            try
            {
                CreateWatchersLocked();
            }
            catch
            {
                // If reconstruction fails (e.g. directory vanished), the
                // RepositoryService will surface RepositoryLost separately.
            }
        }
        _debouncer.OnRawEvent(RepositoryChangeKind.BufferOverflow);
    }

    private void FireChanged(RepositoryChangeKind kind)
    {
        Changed?.Invoke(this, new RepositoryChangedEventArgs(kind, DateTime.UtcNow));
    }

    /// <summary>
    /// Convert an absolute Windows-style path to a repo-relative
    /// forward-slash path suitable for passing to LibGit2Sharp's
    /// <c>repo.Ignore.IsPathIgnored</c>. Returns the empty string if
    /// the path doesn't resolve under the working directory.
    /// </summary>
    internal string ToRepoRelativeForwardSlash(string absolutePath)
    {
        try
        {
            string full = Path.GetFullPath(absolutePath);
            if (!full.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            string rel = full.Substring(_workingDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool IsInsideGitDir(string absolutePath)
    {
        try
        {
            string full = Path.GetFullPath(absolutePath);
            return full.StartsWith(_gitDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True if the given file name (no directory) is one of the well-known
    /// state-tracking files we care about inside <c>.git</c>. Drops
    /// everything else (packfiles, reflogs, FETCH_HEAD, lock files, etc.)
    /// to keep the event volume low.
    /// </summary>
    public static bool IsTrackedGitDirFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        // Lock files bracket every git ref update; they're noise on their own
        // but their presence/absence DOES coincide with the actual ref
        // change immediately preceding/following, so include them so a
        // tightly-spaced lock+unlock burst still triggers the debounce.
        return fileName.Equals("HEAD", StringComparison.Ordinal)
            || fileName.Equals("HEAD.lock", StringComparison.Ordinal)
            || fileName.Equals("index", StringComparison.Ordinal)
            || fileName.Equals("index.lock", StringComparison.Ordinal)
            || fileName.Equals("MERGE_HEAD", StringComparison.Ordinal)
            || fileName.Equals("REBASE_HEAD", StringComparison.Ordinal)
            || fileName.Equals("CHERRY_PICK_HEAD", StringComparison.Ordinal)
            || fileName.Equals("REVERT_HEAD", StringComparison.Ordinal)
            || fileName.StartsWith("BISECT_", StringComparison.Ordinal);
    }
}
