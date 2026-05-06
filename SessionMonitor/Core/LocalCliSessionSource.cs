using System.Collections.Concurrent;
using System.IO;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Watches <c>~/.copilot/session-state/</c>: for each session subfolder,
/// loads its workspace.yaml, tails events.jsonl, probes the inuse lock file,
/// and refreshes git status. Raises <see cref="SessionsChanged"/> on every
/// update so the aggregator can recompute derived state.
/// </summary>
public sealed class LocalCliSessionSource : ISessionSource
{
    public static string DefaultRootPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "session-state");

    private readonly string _root;
    private readonly TimeSpan _staleThreshold;
    private readonly FileSystemWatcher _rootWatcher;
    private readonly FileSystemWatcher _eventsWatcher;
    private readonly System.Timers.Timer _eventsDebounce;
    private readonly ConcurrentDictionary<string, SessionEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly GitStatusProbe _git = new();
    private readonly System.Timers.Timer _heartbeat;
    private readonly object _refreshGate = new();
    private bool _disposed;

    public LocalCliSessionSource(string? root = null, TimeSpan? staleThreshold = null, TimeSpan? heartbeatInterval = null)
    {
        _root = root ?? DefaultRootPath;
        _staleThreshold = staleThreshold ?? TimeSpan.FromHours(48);
        Directory.CreateDirectory(_root);

        Rescan();

        _rootWatcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _rootWatcher.Created += (_, _) => ScheduleRescan();
        _rootWatcher.Deleted += (_, _) => ScheduleRescan();
        _rootWatcher.Renamed += (_, _) => ScheduleRescan();

        // Per-session events.jsonl watcher. Tool calls from short-running tools
        // (edit/create complete in ms) start and finish between heartbeat ticks
        // — invisible to a 5 s poll. Watching events.jsonl directly closes that
        // window: we pump within ~30 ms of each line being written, which is
        // fast enough that an in-flight edit is observably in-flight before
        // its complete event arrives.
        _eventsWatcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            Filter = "events.jsonl",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
            InternalBufferSize = 64 * 1024,
        };
        // Short trailing-edge debounce: long enough to coalesce a burst of writes
        // from a single tool call (each line is its own write), short enough that
        // we still catch the gap between a tool's start and complete events.
        _eventsDebounce = new System.Timers.Timer(30) { AutoReset = false };
        _eventsDebounce.Elapsed += (_, _) => Refresh();
        FileSystemEventHandler bump = (_, _) =>
        {
            _eventsDebounce.Stop();
            _eventsDebounce.Start();
        };
        _eventsWatcher.Changed += bump;
        _eventsWatcher.Created += bump;

        _heartbeat = new System.Timers.Timer((heartbeatInterval ?? TimeSpan.FromSeconds(5)).TotalMilliseconds) { AutoReset = true };
        _heartbeat.Elapsed += (_, _) => Refresh();
        _heartbeat.Start();
    }

    /// <summary>Live-tunable heartbeat interval — used by the Settings UI.</summary>
    public TimeSpan HeartbeatInterval
    {
        get => TimeSpan.FromMilliseconds(_heartbeat.Interval);
        set
        {
            if (value.TotalMilliseconds < 500) value = TimeSpan.FromMilliseconds(500);
            _heartbeat.Interval = value.TotalMilliseconds;
        }
    }

    public event EventHandler? SessionsChanged;

    public IReadOnlyCollection<SessionState> Snapshot()
    {
        return _entries.Values.Select(e => e.State).ToArray();
    }

    private long _heartbeatCount;

    public void Refresh()
    {
        if (_disposed) return;
        if (!Monitor.TryEnter(_refreshGate)) return;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int tickNo = (int)Interlocked.Increment(ref _heartbeatCount);
            foreach (var entry in _entries.Values)
            {
                RefreshEntry(entry);
            }
            // Always raise: stale-session detection in the state machine depends on
            // "now", which changes every tick even if no per-entry data did.
            RaiseChanged();

            if (tickNo % 12 == 1)
            {
                int alive = 0, total = _entries.Count;
                foreach (var e in _entries.Values)
                {
                    if (e.State.LockFilePresent && e.State.PidAlive) alive++;
                }
                CopilotSessionMonitor.DebugLog.Info($"heartbeat #{tickNo} took {sw.ElapsedMilliseconds}ms, {alive}/{total} alive");

                // Surface any silent failures accumulated since the last flush.
                ErrorTally.Flush((key, count) =>
                    CopilotSessionMonitor.DebugLog.Info($"swallowed {count}x {key}"));
            }
        }
        finally
        {
            Monitor.Exit(_refreshGate);
        }
    }

    private void ScheduleRescan()
    {
        // Coalesce rapid-fire FSW events; rescan happens on the next heartbeat anyway,
        // but kick a full rescan immediately so new sessions show fast.
        Task.Run(() =>
        {
            try { Rescan(); RaiseChanged(); } catch { /* ignore */ }
        });
    }

    private void Rescan()
    {
        if (!Directory.Exists(_root)) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(dir.TrimEnd('\\', '/'));
            seen.Add(id);

            if (!_entries.TryGetValue(id, out var entry))
            {
                var state = new SessionState { SessionId = id, SessionDirectory = dir };
                entry = new SessionEntry(state, new SessionTailer(state));
                if (!_entries.TryAdd(id, entry))
                {
                    entry.Dispose();
                    continue;
                }
            }
            RefreshEntry(entry);
        }

        // Drop entries whose folders no longer exist.
        foreach (var kv in _entries)
        {
            if (!seen.Contains(kv.Key))
            {
                if (_entries.TryRemove(kv.Key, out var removed))
                    removed.Dispose();
            }
        }
    }

    /// <summary>Returns true if anything user-visible changed for this entry.</summary>
    private bool RefreshEntry(SessionEntry entry)
    {
        var s = entry.State;
        bool changed = false;

        // Workspace YAML — only re-read on mtime change.
        var ws = Path.Combine(s.SessionDirectory, "workspace.yaml");
        if (File.Exists(ws))
        {
            var mtime = File.GetLastWriteTimeUtc(ws);
            if (mtime != entry.WorkspaceMtimeUtc)
            {
                if (WorkspaceLoader.TryLoad(ws, s))
                {
                    entry.WorkspaceMtimeUtc = mtime;
                    changed = true;
                }
            }
        }

        // Liveness.
        var prevLockPid = s.LockPid;
        var prevLockPresent = s.LockFilePresent;
        var prevPidAlive = s.PidAlive;
        PidLiveness.Probe(s);
        if (s.LockPid != prevLockPid || s.LockFilePresent != prevLockPresent || s.PidAlive != prevPidAlive) changed = true;

        // Tail events. Only do the (potentially large) initial fold for sessions
        // that are alive — offline sessions can keep their last-known summary.
        if (s.LockFilePresent && s.PidAlive)
        {
            try { entry.Tailer.Pump(); } catch { ErrorTally.Tally("source.tailerPump"); }
        }
        else if (!entry.InitialOfflineFoldDone)
        {
            // For offline sessions, do a single light fold once to populate
            // baseline metadata (LastEventAt, BaseCommit) but never again.
            try { entry.Tailer.Pump(); } catch { ErrorTally.Tally("source.tailerPump"); }
            entry.InitialOfflineFoldDone = true;
        }

        // Git status (cached; only refresh for live sessions to avoid spinning up git
        // for every historical offline session on every heartbeat tick).
        if (!string.IsNullOrEmpty(s.Cwd) && s.LockFilePresent && s.PidAlive)
        {
            if (_git.TryGetCached(s.Cwd!, out var dirty, out var at))
            {
                if (dirty != s.GitDirty)
                {
                    s.GitDirty = dirty;
                    changed = true;
                }
                s.GitDirtyCheckedAt = at;
            }
            else
            {
                _ = _git.RefreshAsync(s.Cwd!);
            }
        }
        else if (s.GitDirty)
        {
            // Session went offline: don't keep showing it as Red.
            s.GitDirty = false;
            changed = true;
        }

        // Auto-prune very stale offline sessions so the UI doesn't drown in history.
        if (!s.LockFilePresent && DateTimeOffset.UtcNow - s.LastActivity > _staleThreshold)
        {
            if (_entries.TryRemove(s.SessionId, out var removed))
            {
                removed.Dispose();
                changed = true;
            }
        }

        return changed;
    }

    private void RaiseChanged() => SessionsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeat.Stop();
        _heartbeat.Dispose();
        _eventsDebounce.Stop();
        _eventsDebounce.Dispose();
        _eventsWatcher.Dispose();
        _rootWatcher.Dispose();
        foreach (var e in _entries.Values) e.Dispose();
        _entries.Clear();
    }

    private sealed class SessionEntry : IDisposable
    {
        public SessionEntry(SessionState state, SessionTailer tailer)
        {
            State = state;
            Tailer = tailer;
        }
        public SessionState State { get; }
        public SessionTailer Tailer { get; }
        public DateTime WorkspaceMtimeUtc { get; set; } = DateTime.MinValue;
        public bool InitialOfflineFoldDone { get; set; }

        public void Dispose() => Tailer.Dispose();
    }
}
