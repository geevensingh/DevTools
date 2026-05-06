using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using CopilotSessionMonitor.Core;

namespace CopilotSessionMonitor.Services;

public enum NotificationSound
{
    None, Default, Asterisk, Beep, Question, Exclamation, Hand,
}

/// <summary>
/// Maps state transitions to user notifications.
///
/// Behaviors:
///   - any -> Blue       fires a "needs you" toast (focuses terminal on click)
///   - Red -> Green      fires a "finished" toast (focuses terminal on click)
///   - Multiple same-type transitions within 500 ms coalesce into one
///     aggregate toast ("N sessions are waiting on you"). Aggregate clicks
///     open the main window since there's no single session to focus.
///   - Toasts are suppressed when Windows reports the user is in DND /
///     Focus Assist / a presentation / a fullscreen game etc.
///   - Each session-scoped transition asks the main window VM to pre-expand
///     its row, even if the window is hidden, so when the user does open
///     the window the relevant row is already open.
///   - Sounds are played via System.Media.SystemSounds (so they respect the
///     user's Windows sound theme); the balloon's built-in sound is
///     suppressed by passing NotificationIcon.None.
/// </summary>
public sealed class Notifier
{
    private readonly TaskbarIcon _trayIcon;
    private readonly Dictionary<string, DateTimeOffset> _lastFiredPerSession = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(500);

    private readonly List<StatusTransition> _buffer = new();
    private readonly DispatcherTimer _coalesceTimer;

    public bool BlueEnabled { get; set; } = true;
    public bool RedToGreenEnabled { get; set; } = true;
    public NotificationSound BlueSound { get; set; } = NotificationSound.Asterisk;
    public NotificationSound RedToGreenSound { get; set; } = NotificationSound.Default;

    /// <summary>Optional mute service. When set, transitions for muted sessions are dropped (no toast, no pre-expand).</summary>
    public MuteService? MuteService { get; set; }

    /// <summary>The session whose last-fired single toast is currently visible.
    /// <c>null</c> when an aggregate toast is showing or no toast is up.</summary>
    public string? LastToastSessionId { get; private set; }

    /// <summary>Optional callback invoked for every session-scoped transition we'd
    /// notify on, regardless of whether the toast is suppressed by DND. Lets the
    /// VM pre-expand the matching row so the user finds it ready when they open
    /// the window. Receives the session id.</summary>
    public Action<string>? PreExpandRow { get; set; }

    public Notifier(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
        _trayIcon.TrayBalloonTipClosed += (_, _) => LastToastSessionId = null;

        _coalesceTimer = new DispatcherTimer { Interval = CoalesceWindow };
        _coalesceTimer.Tick += (_, _) => { _coalesceTimer.Stop(); FlushBuffer(); };
    }

    public void HandleTransitions(IReadOnlyList<StatusTransition> transitions)
    {
        if (transitions.Count == 0) return;

        // Side-effect that runs *before* dedupe / coalescing: pre-expand the
        // row for any session whose transition we'd notify on. Even if the
        // toast is later suppressed by DND, the VM update is fine to do.
        foreach (var t in transitions)
        {
            if (IsRelevant(t)) PreExpandRow?.Invoke(t.Session.SessionId);
        }

        lock (_buffer)
        {
            foreach (var t in transitions)
            {
                if (IsRelevant(t)) _buffer.Add(t);
            }
            if (_buffer.Count == 0) return;
        }

        // (Re)start the coalescing timer on the UI thread.
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
        {
            _coalesceTimer.Stop();
            _coalesceTimer.Start();
        }
        else
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _coalesceTimer.Stop();
                _coalesceTimer.Start();
            });
        }
    }

    private bool IsRelevant(StatusTransition t)
    {
        if (MuteService is not null && MuteService.IsMuted(t.Session.SessionId)) return false;
        if (BlueEnabled && t.To == SessionStatus.Blue && t.From != SessionStatus.Blue) return true;
        if (RedToGreenEnabled && t.From == SessionStatus.Red && t.To == SessionStatus.Green) return true;
        return false;
    }

    private void FlushBuffer()
    {
        List<StatusTransition> snapshot;
        lock (_buffer)
        {
            if (_buffer.Count == 0) return;
            snapshot = new List<StatusTransition>(_buffer);
            _buffer.Clear();
        }

        // Dedupe per-session within the rolling window. A session that already
        // got a toast 3 seconds ago doesn't get another one during a flap.
        var now = DateTimeOffset.UtcNow;
        snapshot = snapshot.Where(t =>
        {
            if (_lastFiredPerSession.TryGetValue(t.Session.SessionId, out var last) &&
                now - last < DedupeWindow) return false;
            return true;
        }).ToList();
        if (snapshot.Count == 0) return;

        // Don't even bother showing toasts if the user is in DND / fullscreen.
        // Pre-expand already happened above, so they'll find the row open
        // when they come back.
        if (!UserAcceptsNotifications()) return;

        foreach (var t in snapshot) _lastFiredPerSession[t.Session.SessionId] = now;

        // Group by toast type, then either fire single or aggregate per group.
        var blueGroup = snapshot.Where(t => t.To == SessionStatus.Blue && t.From != SessionStatus.Blue).ToList();
        var greenGroup = snapshot.Where(t => t.From == SessionStatus.Red && t.To == SessionStatus.Green).ToList();

        if (blueGroup.Count == 1)
        {
            var t = blueGroup[0];
            ShowSingle(t.Session.SessionId,
                "A session needs your input",
                $"{t.Session.DisplayTitle}\n{Truncate(t.Session.LastAskUserQuestion ?? "Waiting on ask_user")}",
                BlueSound);
        }
        else if (blueGroup.Count > 1)
        {
            ShowAggregate(
                $"{blueGroup.Count} sessions need your input",
                string.Join('\n', blueGroup.Select(t => $"\u2022 {t.Session.DisplayTitle}")),
                BlueSound);
        }

        if (greenGroup.Count == 1)
        {
            var t = greenGroup[0];
            ShowSingle(t.Session.SessionId,
                "Session finished its changes",
                $"{t.Session.DisplayTitle} went idle in {t.Session.DisplayRepo}.",
                RedToGreenSound);
        }
        else if (greenGroup.Count > 1)
        {
            ShowAggregate(
                $"{greenGroup.Count} sessions finished their changes",
                string.Join('\n', greenGroup.Select(t => $"\u2022 {t.Session.DisplayTitle}")),
                RedToGreenSound);
        }
    }

    private void ShowSingle(string sessionId, string title, string body, NotificationSound sound)
    {
        LastToastSessionId = sessionId;
        ShowToast(title, body, sound);
    }

    private void ShowAggregate(string title, string body, NotificationSound sound)
    {
        // Aggregate toasts have no single session focus target; the click
        // handler will fall back to opening the main window.
        LastToastSessionId = null;
        ShowToast(title, body, sound);
    }

    private void ShowToast(string title, string body, NotificationSound sound)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                // NotificationIcon.None suppresses the balloon's default system
                // sound so we can play our own (and not double up).
                _trayIcon.ShowNotification(
                    title: title,
                    message: body,
                    icon: NotificationIcon.None);
                PlaySound(sound);
            }
            catch
            {
                // Balloons fail silently on stripped-down Windows; sound still useful.
                try { PlaySound(sound); } catch { /* nothing to do */ }
            }
        });
    }

    private static void PlaySound(NotificationSound s)
    {
        switch (s)
        {
            case NotificationSound.None: return;
            case NotificationSound.Default:
            case NotificationSound.Beep: SystemSounds.Beep.Play(); return;
            case NotificationSound.Asterisk: SystemSounds.Asterisk.Play(); return;
            case NotificationSound.Question: SystemSounds.Question.Play(); return;
            case NotificationSound.Exclamation: SystemSounds.Exclamation.Play(); return;
            case NotificationSound.Hand: SystemSounds.Hand.Play(); return;
        }
    }

    /// <summary>Honor Windows Focus Assist / DND / fullscreen game / presentation mode.</summary>
    private static bool UserAcceptsNotifications()
    {
        try
        {
            int hr = SHQueryUserNotificationState(out int state);
            if (hr != 0) return true; // best-effort; if the call fails, allow the toast
            // QUNS_ACCEPTS_NOTIFICATIONS = 5
            return state == 5;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "\u2026";
}

