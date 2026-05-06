namespace CopilotSessionMonitor.Core;

/// <summary>
/// Owns one or more <see cref="ISessionSource"/>s, runs the state machine on
/// every update, and emits <see cref="StateChanged"/> events that include the
/// previous status for each session — used by <c>Notifier</c> to fire toasts on
/// specific transitions.
/// </summary>
public sealed class SessionAggregator : IDisposable
{
    private readonly List<ISessionSource> _sources = new();
    private readonly Dictionary<string, SessionStatus> _previousStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _firstUpdateConsumed;

    public event EventHandler<SessionsUpdatedEventArgs>? Updated;

    public void Add(ISessionSource source)
    {
        _sources.Add(source);
        source.SessionsChanged += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Recompute();

    public void Recompute()
    {
        var snapshot = _sources.SelectMany(s => s.Snapshot()).ToArray();
        var transitions = new List<StatusTransition>();
        var now = DateTimeOffset.UtcNow;
        bool isFirst;
        lock (_gate)
        {
            isFirst = !_firstUpdateConsumed;
            foreach (var s in snapshot)
            {
                var newStatus = SessionStateMachine.Classify(s);
                _previousStatus.TryGetValue(s.SessionId, out var prev);
                bool hadPrev = _previousStatus.ContainsKey(s.SessionId);

                if (!hadPrev || prev != newStatus)
                {
                    s.EnteredStatusAt = now;
                    if (!isFirst && hadPrev && prev != newStatus)
                    {
                        transitions.Add(new StatusTransition(s, prev, newStatus));
                    }
                }
                _previousStatus[s.SessionId] = newStatus;
                s.DerivedStatus = newStatus;
            }

            // Drop transitions for sessions that disappeared.
            var alive = new HashSet<string>(snapshot.Select(s => s.SessionId), StringComparer.OrdinalIgnoreCase);
            foreach (var key in _previousStatus.Keys.ToArray())
            {
                if (!alive.Contains(key)) _previousStatus.Remove(key);
            }
            _firstUpdateConsumed = true;
        }

        Updated?.Invoke(this, new SessionsUpdatedEventArgs(snapshot, transitions));
    }

    public void Dispose()
    {
        foreach (var s in _sources)
        {
            s.SessionsChanged -= OnSourceChanged;
            s.Dispose();
        }
        _sources.Clear();
    }
}

public sealed record StatusTransition(SessionState Session, SessionStatus From, SessionStatus To);

public sealed class SessionsUpdatedEventArgs : EventArgs
{
    public IReadOnlyList<SessionState> Sessions { get; }
    public IReadOnlyList<StatusTransition> Transitions { get; }
    public SessionsUpdatedEventArgs(IReadOnlyList<SessionState> sessions, IReadOnlyList<StatusTransition> transitions)
    {
        Sessions = sessions;
        Transitions = transitions;
    }
}
