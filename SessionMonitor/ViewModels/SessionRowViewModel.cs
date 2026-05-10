using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionMonitor.Core;
using CopilotSessionMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace CopilotSessionMonitor.ViewModels;

/// <summary>One bound row in the session list.</summary>
public sealed partial class SessionRowViewModel : ObservableObject
{
    [ObservableProperty] private string _searchHaystack = "";
    [ObservableProperty] private bool _isAttention;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private string _muteIcon = "🔔";
    [ObservableProperty] private string _muteTooltip = "Mute notifications for this session";
    [ObservableProperty] private string _timeInStateText = "";
    [ObservableProperty] private string _tokensText = "";
    [ObservableProperty] private DateTimeOffset _lastActivity = DateTimeOffset.MinValue;
    [ObservableProperty] private bool _isTimelineExpanded;
    [ObservableProperty] private string _timelineToggleLabel = "▶ Recent activity";
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<TimelineEntry> _timeline = new();

    /// <summary>Mute service (set by the parent VM after construction).</summary>
    public CopilotSessionMonitor.Services.MuteService? MuteService { get; set; }

    [ObservableProperty] private string _displayTitle = "";
    [ObservableProperty] private string _repository = "";
    [ObservableProperty] private string _branch = "";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private SessionStatus _status;
    [ObservableProperty] private Brush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _whenText = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isOffline;
    [ObservableProperty] private string? _previewBlock;
    [ObservableProperty] private string? _killLabel;

    public string SessionId { get; }
    public string SessionDirectory { get; }
    public int? Pid { get; private set; }
    public string? Cwd { get; private set; }
    public string? Summary { get; private set; }
    public IReadOnlyCollection<string> KnownTabTitles { get; private set; } = Array.Empty<string>();

    public SessionRowViewModel(string sessionId, string sessionDirectory)
    {
        SessionId = sessionId;
        SessionDirectory = sessionDirectory;
    }

    public void UpdateFrom(SessionState s, bool wasExpanded)
    {
        DisplayTitle = s.DisplayTitle;
        Repository = s.DisplayRepo;
        Branch = !string.IsNullOrEmpty(s.LiveBranch) ? s.LiveBranch! : (s.Branch ?? "");
        Status = s.DerivedStatus;
        IsOffline = s.DerivedStatus == SessionStatus.Offline;
        IsAttention = s.DerivedStatus == SessionStatus.Red || s.DerivedStatus == SessionStatus.Blue;
        IsMuted = MuteService?.IsMuted(SessionId) ?? false;
        MuteIcon = IsMuted ? "🔕" : "🔔";
        MuteTooltip = IsMuted
            ? "Notifications muted for this session — click to unmute"
            : "Mute notifications for this session";

        TimeInStateText = s.EnteredStatusAt is { } enter
            ? $"for {HumanShort(DateTimeOffset.UtcNow - enter)}"
            : "";

        TokensText = s.OutputTokens > 0 ? $"{s.OutputTokens:N0} output tokens" : "";

        LastActivity = s.LastActivity;

        // Project the recent-events queue onto the bound timeline only when
        // the row is expanded AND the user has opted into the timeline view —
        // keeps cost off the hot path.
        if (wasExpanded && IsTimelineExpanded)
        {
            Timeline.Clear();
            foreach (var e in s.RecentEvents)
            {
                Timeline.Add(new TimelineEntry(
                    HumanWhen(e.At),
                    e.Kind switch
                    {
                        Core.RecentEventKind.Tool => "🔧",
                        Core.RecentEventKind.AssistantMessage => "🤖",
                        Core.RecentEventKind.UserMessage => "👤",
                        _ => "·",
                    },
                    e.Text));
            }
        }

        // Keep the toggle label up-to-date with the event count even when
        // the timeline itself is collapsed, so users can see how active the
        // session has been at a glance.
        TimelineToggleLabel = $"{(IsTimelineExpanded ? "▼" : "▶")} Recent activity ({s.RecentEvents.Count})";
        Pid = s.LockPid;
        Cwd = s.Cwd;
        Summary = s.Summary;
        // Snapshot the title set so the focuser can match without taking a lock
        // on the underlying SessionState.
        KnownTabTitles = s.KnownTabTitles.ToArray();
        StatusBrush = StatusBrushFor(s.DerivedStatus);
        StatusLine = BuildStatusLine(s);
        WhenText = HumanWhen(s.LastActivity);
        KillLabel = s.LockPid is { } pid ? $"⛔ Kill PID {pid}" : "⛔ Kill PID";
        PreviewBlock = wasExpanded ? BuildPreview(s) : PreviewBlock;
        IsExpanded = wasExpanded;

        // Lower-case haystack used by the word-wheel filter. Updating it here
        // (rather than in a getter) keeps the filter predicate cheap.
        SearchHaystack = string.Join(' ',
            DisplayTitle, Repository, Branch, StatusLine,
            s.Cwd ?? "", s.SessionId,
            s.CloudSessionId ?? "", s.CloudTaskId ?? "").ToLowerInvariant();
    }

    private static string BuildStatusLine(SessionState s)
    {
        return s.DerivedStatus switch
        {
            SessionStatus.Blue when !string.IsNullOrEmpty(s.LastAskUserQuestion)
                => $"Waiting on ask_user — \u201C{Truncate(s.LastAskUserQuestion!, 90)}\u201D",
            SessionStatus.Blue => "Waiting on ask_user",
            SessionStatus.Red when InFlightChange(s, out var label) => label!,
            SessionStatus.Red => "Editing\u2026",
            SessionStatus.Yellow when s.LastToolName is not null
                => Suffix($"{s.LastToolName}{(s.LastToolDescription is null ? "" : $" \u00B7 {Truncate(s.LastToolDescription!, 70)}")}", s.GitDirty),
            SessionStatus.Yellow => Suffix("Thinking\u2026", s.GitDirty),
            SessionStatus.Green => Suffix("Idle \u00B7 waiting on you", s.GitDirty),
            SessionStatus.Offline when s.LockFilePresent && !s.PidAlive => $"Orphaned \u00B7 PID {s.LockPid} not running",
            SessionStatus.Offline => "Offline \u00B7 exited",
            _ => "",
        };
    }

    private static string Suffix(string main, bool gitDirty) =>
        gitDirty ? $"{main} \u00B7 working tree dirty" : main;

    private static bool InFlightChange(SessionState s, out string? label)
    {
        foreach (var t in s.InFlightTools.Values)
        {
            if (string.Equals(t.ToolName, "edit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.ToolName, "create", StringComparison.OrdinalIgnoreCase))
            {
                label = t.Description is null ? $"Editing\u2026" : $"{t.ToolName} \u00B7 {Truncate(t.Description, 80)}";
                return true;
            }
        }
        label = null;
        return false;
    }

    private static string BuildPreview(SessionState s)
    {
        var sb = new System.Text.StringBuilder();
        if (s.LastToolName is not null)
        {
            sb.Append("tool: ").Append(s.LastToolName);
            if (s.LastToolDescription is not null) sb.Append(" — ").Append(s.LastToolDescription);
            sb.AppendLine();
        }
        if (s.BaseCommit is not null) sb.Append("baseCommit: ").AppendLine(s.BaseCommit[..Math.Min(8, s.BaseCommit.Length)]);
        if (s.GitDirty) sb.AppendLine("git: working tree dirty");
        if (!string.IsNullOrEmpty(s.Cwd)) sb.Append("cwd: ").AppendLine(s.Cwd);
        if (s.LockPid is { } pid) sb.Append("pid: ").AppendLine(pid.ToString());
        sb.Append("session: ").AppendLine(s.SessionId);
        if (!string.IsNullOrEmpty(s.CloudSessionId)) sb.Append("cloud session: ").AppendLine(s.CloudSessionId);
        if (!string.IsNullOrEmpty(s.CloudTaskId)) sb.Append("cloud task: ").Append(s.CloudTaskId);
        else sb.Length -= Environment.NewLine.Length; // strip trailing newline from session line if no task id follows
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "\u2026";

    private static string HumanWhen(DateTimeOffset when)
    {
        if (when == DateTimeOffset.MinValue) return "";
        var d = DateTimeOffset.UtcNow - when.ToUniversalTime();
        if (d.TotalSeconds < 5) return "now";
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s ago";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }

    private static string HumanShort(TimeSpan d)
    {
        if (d.TotalSeconds < 5) return "now";
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h {d.Minutes}m";
        return $"{(int)d.TotalDays}d {d.Hours}h";
    }

    public static Brush StatusBrushFor(SessionStatus s) => s switch
    {
        SessionStatus.Red => new SolidColorBrush(Color.FromRgb(0xE7, 0x48, 0x56)),
        SessionStatus.Blue => new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
        SessionStatus.Yellow => new SolidColorBrush(Color.FromRgb(0xF7, 0xC3, 0x31)),
        SessionStatus.Green => new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F)),
        _ => new SolidColorBrush(Color.FromRgb(0x6E, 0x71, 0x77)),
    };

    [RelayCommand]
    private void ToggleTimeline()
    {
        IsTimelineExpanded = !IsTimelineExpanded;
        TimelineToggleLabel = (IsTimelineExpanded ? "▼" : "▶") +
            TimelineToggleLabel.Substring(1);
    }

    [RelayCommand]
    private static void CopyTimelineEntry(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); } catch { /* clipboard sometimes flakes */ }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (MuteService is null) return;
        MuteService.Toggle(SessionId);
        IsMuted = MuteService.IsMuted(SessionId);
        MuteIcon = IsMuted ? "🔕" : "🔔";
        MuteTooltip = IsMuted
            ? "Notifications muted for this session — click to unmute"
            : "Mute notifications for this session";
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void FocusTerminal()
    {
        if (Pid is { } pid) TerminalFocuser.TryFocus(pid, Summary ?? DisplayTitle, Cwd, KnownTabTitles);
    }

    [RelayCommand]
    private void CopySessionId()
    {
        try { System.Windows.Clipboard.SetText(SessionId); } catch { /* clipboard sometimes flakes */ }
    }

    [RelayCommand]
    private void OpenInVsCode()
    {
        if (!string.IsNullOrEmpty(Cwd)) VsCodeLauncher.TryOpen(Cwd!);
    }

    [RelayCommand]
    private void OpenSessionState()
    {
        VsCodeLauncher.TryOpen(SessionDirectory);
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        var target = !string.IsNullOrEmpty(Cwd) && System.IO.Directory.Exists(Cwd) ? Cwd : SessionDirectory;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{target}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = false,
            });
        }
        catch { /* explorer.exe should always be available */ }
    }

    [RelayCommand]
    private void KillProcess()
    {
        if (Pid is not { } pid) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Kill PID {pid} ({DisplayTitle})?\n\nThis terminates the entire process tree.",
            "Confirm kill",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.OK) return;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch { /* process likely already exited */ }
    }
}
