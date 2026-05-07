using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using CopilotSessionMonitor.Core;
using CopilotSessionMonitor.Services;
using CopilotSessionMonitor.ViewModels;
using CopilotSessionMonitor.Views;

namespace CopilotSessionMonitor;

/// <summary>
/// Owns the tray icon, the data sources, the aggregator, the main window,
/// and the notifier. Lives for the lifetime of the app.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private TaskbarIcon? _tray;
    private SessionAggregator? _aggregator;
    private MainWindowViewModel? _vm;
    private MainWindow? _window;
    private Notifier? _notifier;
    private AppSettings? _settings;
    private MuteService? _muteService;
    private LocalCliSessionSource? _localSource;
    private SessionStatus _currentTrayState = SessionStatus.Offline;
    private Icon? _currentIcon;

    public void Start()
    {
        _settings = AppSettings.Load();
        ApplySettingsGlobals(_settings);
        _muteService = new MuteService(_settings, () => _settings.Save());

        _vm = new MainWindowViewModel { MuteService = _muteService };
        _window = new MainWindow(_vm, _settings);
        _window.Closing += (_, e) => { e.Cancel = true; _window.Hide(); };
        _window.SettingsRequested += (_, _) => OpenSettings();

        _tray = new TaskbarIcon
        {
            ToolTipText = "Copilot Session Monitor",
            ContextMenu = BuildContextMenu(),
        };
        _tray.LeftClickCommand = new RelayCommandSimple(ToggleWindow);
        _tray.NoLeftClickDelay = true;

        // We must give the icon an initial image before ForceCreate, otherwise
        // Shell_NotifyIcon refuses to register a "blank" icon and we get a
        // silent no-op (no icon ever appears).
        UpdateTrayIcon(SessionStatus.Offline);

        // Without this, a programmatically-constructed (i.e. not-from-XAML)
        // TaskbarIcon never adds itself to the system tray. Took me an
        // embarrassing amount of debugging the first time around.
        _tray.ForceCreate(enablesEfficiencyMode: false);

        // Toast click routing:
        //   - welcome toast (no LastToastSessionId): show the main window
        //   - aggregate toast (LastToastSessionId is null because >1 session
        //     coalesced): also show the main window — there's no single
        //     terminal to focus
        //   - single session toast: focus that session's terminal tab
        _tray.TrayBalloonTipClicked += (_, _) =>
        {
            var sid = _notifier?.LastToastSessionId;
            if (string.IsNullOrEmpty(sid))
            {
                ToggleWindow();
                return;
            }
            FocusSessionTerminal(sid!);
        };

        _notifier = new Notifier(_tray);
        _notifier.BlueEnabled = _settings.NotifyOnBlue;
        _notifier.RedToGreenEnabled = _settings.NotifyOnRedToGreen;
        _notifier.BlueSound = ParseSound(_settings.BlueSound, NotificationSound.Asterisk);
        _notifier.RedToGreenSound = ParseSound(_settings.RedToGreenSound, NotificationSound.Default);
        _notifier.PreExpandRow = sid =>
        {
            // Marshal to UI thread; Notifier may invoke this from the
            // dispatcher already but be defensive.
            System.Windows.Application.Current?.Dispatcher.Invoke(() => _vm?.PreExpand(sid));
        };

        _aggregator = new SessionAggregator();
        _aggregator.Updated += OnAggregatorUpdated;
        _localSource = new LocalCliSessionSource(
            heartbeatInterval: TimeSpan.FromSeconds(_settings.HeartbeatSeconds));
        _aggregator.Add(_localSource);
        _aggregator.Recompute();

        // First-run greeting so the user can confirm the icon registered.
        // Windows 11 hides new tray icons under the ^ chevron — show a balloon
        // pointing the user there.
        try
        {
            _tray.ShowNotification(
                title: "Copilot Session Monitor is running",
                message: "Look for the tray icon under the ^ chevron in your taskbar, and drag it out to keep it visible.",
                icon: H.NotifyIcon.Core.NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            DebugLog.Error("ShowNotification(welcome) failed", ex);
        }
    }

    private void OpenSettings()
    {
        if (_settings is null) return;
        var dlg = new SettingsWindow(_settings);
        if (_window is { IsVisible: true })
        {
            dlg.Owner = _window;
        }
        else
        {
            dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        }
        if (dlg.ShowDialog() == true && dlg.Result is { } updated)
        {
            // Persist the user-tunable subset onto the canonical instance and save.
            _settings.HeartbeatSeconds = updated.HeartbeatSeconds;
            _settings.StaleThresholdHours = updated.StaleThresholdHours;
            _settings.NotifyOnBlue = updated.NotifyOnBlue;
            _settings.NotifyOnRedToGreen = updated.NotifyOnRedToGreen;
            _settings.BlueSound = updated.BlueSound;
            _settings.RedToGreenSound = updated.RedToGreenSound;
            _settings.Save();
            ApplySettingsLive(_settings);
        }
    }

    /// <summary>Apply tunables that are read once at startup (or that need a reapply on Save).</summary>
    private static void ApplySettingsGlobals(AppSettings s)
    {
        SessionStateMachine.StaleThreshold = TimeSpan.FromHours(Math.Max(1, s.StaleThresholdHours));
    }

    /// <summary>Apply tunables that are read by long-lived components.</summary>
    private void ApplySettingsLive(AppSettings s)
    {
        ApplySettingsGlobals(s);
        if (_localSource is not null)
            _localSource.HeartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, s.HeartbeatSeconds));
        if (_notifier is not null)
        {
            _notifier.BlueEnabled = s.NotifyOnBlue;
            _notifier.RedToGreenEnabled = s.NotifyOnRedToGreen;
            _notifier.BlueSound = ParseSound(s.BlueSound, NotificationSound.Asterisk);
            _notifier.RedToGreenSound = ParseSound(s.RedToGreenSound, NotificationSound.Default);
        }
        // Force an immediate recompute so any classification change (e.g.
        // bumping the stale threshold) is reflected at once.
        _aggregator?.Recompute();
    }

    private static NotificationSound ParseSound(string value, NotificationSound fallback) =>
        Enum.TryParse<NotificationSound>(value, ignoreCase: true, out var s) ? s : fallback;

    /// <summary>
    /// Routes a toast click for a specific session to its terminal.
    /// We look up the row VM (which already carries the live snapshot of
    /// summary, cwd, known tab titles, and the PID) and call the same
    /// TerminalFocuser the per-row Focus button uses. Falls back to
    /// revealing the row in the main window if the session has dropped
    /// out of the live list (e.g. process exited between toast and click).
    /// </summary>
    private void FocusSessionTerminal(string sessionId)
    {
        if (_vm is null) return;
        SessionRowViewModel? row = null;
        foreach (var r in _vm.Sessions)
        {
            if (string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                row = r;
                break;
            }
        }
        if (row?.Pid is { } pid)
        {
            CopilotSessionMonitor.Services.TerminalFocuser.TryFocus(
                pid, row.Summary ?? row.DisplayTitle, row.Cwd, row.KnownTabTitles);
            return;
        }
        // Session no longer alive (or never made it into the VM): fall back
        // to opening the main window and revealing whatever row exists.
        _window?.RevealSession(sessionId);
    }

    private void OnAggregatorUpdated(object? sender, SessionsUpdatedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        dispatcher.Invoke(() =>
        {
            _vm?.ApplyUpdate(e.Sessions);
            UpdateTrayIcon(_vm?.WorstStatus ?? SessionStatus.Offline);
            _notifier?.HandleTransitions(e.Transitions);
            UpdateContextMenuCounts(e.Sessions);
        });
    }

    private void UpdateTrayIcon(SessionStatus status)
    {
        if (_tray is null) return;
        if (status == _currentTrayState && _currentIcon is not null) return;
        _currentTrayState = status;

        var newIcon = TrayIconFactory.Build(status);
        _tray.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;

        _tray.ToolTipText = $"Copilot Session Monitor — {status.DisplayName()}";
    }

    private void ToggleWindow()
    {
        if (_window is null) return;
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.ShowAtTray();
        }
    }

    private System.Windows.Controls.MenuItem? _redCount, _blueCount, _yellowCount, _greenCount;
    private System.Windows.Controls.MenuItem? _autostart;

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var show = new System.Windows.Controls.MenuItem { Header = "Show window" };
        show.Click += (_, _) => ToggleWindow();
        menu.Items.Add(show);

        menu.Items.Add(new System.Windows.Controls.Separator());

        _redCount = new System.Windows.Controls.MenuItem { Header = "—", IsEnabled = false };
        _blueCount = new System.Windows.Controls.MenuItem { Header = "—", IsEnabled = false };
        _yellowCount = new System.Windows.Controls.MenuItem { Header = "—", IsEnabled = false };
        _greenCount = new System.Windows.Controls.MenuItem { Header = "—", IsEnabled = false };
        menu.Items.Add(_redCount);
        menu.Items.Add(_blueCount);
        menu.Items.Add(_yellowCount);
        menu.Items.Add(_greenCount);

        menu.Items.Add(new System.Windows.Controls.Separator());

        _autostart = new System.Windows.Controls.MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = AutostartToggle.IsEnabled,
            StaysOpenOnClick = false,
        };
        _autostart.Click += (_, _) =>
        {
            var nowEnabled = AutostartToggle.Toggle();
            _autostart.IsChecked = nowEnabled;
        };
        menu.Items.Add(_autostart);

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var quit = new System.Windows.Controls.MenuItem { Header = "Quit Session Monitor" };
        quit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    private void UpdateContextMenuCounts(IReadOnlyList<SessionState> sessions)
    {
        int Count(SessionStatus s) => sessions.Count(x => x.DerivedStatus == s);
        if (_redCount is not null) _redCount.Header = $"🔴 {Count(SessionStatus.Red)} making changes";
        if (_blueCount is not null) _blueCount.Header = $"🔵 {Count(SessionStatus.Blue)} need you";
        if (_yellowCount is not null) _yellowCount.Header = $"🟡 {Count(SessionStatus.Yellow)} planning";
        if (_greenCount is not null) _greenCount.Header = $"🟢 {Count(SessionStatus.Green)} idle";
    }

    public void Dispose()
    {
        _window?.FlushSettings();
        _aggregator?.Dispose();
        _tray?.Dispose();
        _currentIcon?.Dispose();
        _window?.Close();
    }

    private sealed class RelayCommandSimple : System.Windows.Input.ICommand
    {
        private readonly Action _act;
        public RelayCommandSimple(Action act) => _act = act;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _act();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
