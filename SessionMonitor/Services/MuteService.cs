namespace CopilotSessionMonitor.Services;

/// <summary>
/// Tracks per-session notification mute state. Both the Notifier (to suppress
/// toasts) and the row VMs (to render the mute icon and toggle it) talk to
/// this single service so they stay in sync without each holding their own
/// copy of the mute set.
/// </summary>
public sealed class MuteService
{
    private readonly AppSettings _settings;
    private readonly Action _persist;

    public event EventHandler<string>? Changed;

    public MuteService(AppSettings settings, Action persist)
    {
        _settings = settings;
        _persist = persist;
    }

    public bool IsMuted(string sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _settings.MutedSessionIds.Contains(sessionId);

    public void Toggle(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        if (!_settings.MutedSessionIds.Add(sessionId))
        {
            _settings.MutedSessionIds.Remove(sessionId);
        }
        _persist();
        Changed?.Invoke(this, sessionId);
    }
}
