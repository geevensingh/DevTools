using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionMonitor.Core;

namespace CopilotSessionMonitor.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Dictionary<string, SessionRowViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();
    public ICollectionView SessionsView { get; }

    /// <summary>Mute service injected by TrayHost; passed to each row VM as it's created.</summary>
    public CopilotSessionMonitor.Services.MuteService? MuteService { get; set; }

    [ObservableProperty] private bool _showOffline;
    [ObservableProperty] private bool _groupByRepo;
    [ObservableProperty] private bool _isPinned = true;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _legendCount = "";
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private SessionStatus _worstStatus = SessionStatus.Offline;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _isFilterEmpty = true;
    [ObservableProperty] private bool _hasNoVisibleRows;

    private string[] _filterTokens = Array.Empty<string>();
    /// <summary>True when ShowOffline is OFF, the filter is non-empty, no online row matches,
    /// but at least one offline row does — graceful fallback to surface them.</summary>
    private bool _showOfflineFallback;
    private bool _suppressExpansionCascade;

    public MainWindowViewModel()
    {
        SessionsView = CollectionViewSource.GetDefaultView(Sessions);
        SessionsView.Filter = item => RowPasses(item as SessionRowViewModel);
        // Primary: worst state first (Red → Blue → Yellow → Green → Offline).
        SessionsView.SortDescriptions.Add(new SortDescription(nameof(SessionRowViewModel.Status), ListSortDirection.Descending));
        // Secondary: most-recently-active first within each status group, so
        // sessions you just touched bubble to the top.
        SessionsView.SortDescriptions.Add(new SortDescription(nameof(SessionRowViewModel.LastActivity), ListSortDirection.Descending));
    }

    partial void OnShowOfflineChanged(bool value) => RecomputeFilter();

    partial void OnGroupByRepoChanged(bool value)
    {
        SessionsView.GroupDescriptions.Clear();
        if (value)
        {
            SessionsView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(SessionRowViewModel.Repository)));
        }
        SessionsView.Refresh();
    }

    partial void OnFilterTextChanged(string value)
    {
        _filterTokens = string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IsFilterEmpty = _filterTokens.Length == 0;
        RecomputeFilter();
    }

    /// <summary>Recompute fallback flag, refresh the view, recompute "no rows" indicator.</summary>
    private void RecomputeFilter()
    {
        // Fallback: only when ShowOffline=off AND filter non-empty AND no online row matches.
        bool fallback = false;
        if (!ShowOffline && _filterTokens.Length > 0)
        {
            bool anyOnline = false;
            foreach (var r in Sessions)
            {
                if (!r.IsOffline && MatchesTokens(r))
                {
                    anyOnline = true;
                    break;
                }
            }
            if (!anyOnline)
            {
                foreach (var r in Sessions)
                {
                    if (r.IsOffline && MatchesTokens(r))
                    {
                        fallback = true;
                        break;
                    }
                }
            }
        }
        _showOfflineFallback = fallback;

        SessionsView.Refresh();
        RecomputeNoVisibleRows();
    }

    private void RecomputeNoVisibleRows()
    {
        bool anyVisible = false;
        foreach (var r in Sessions)
        {
            if (RowPasses(r)) { anyVisible = true; break; }
        }
        HasNoVisibleRows = !anyVisible && Sessions.Count > 0;
    }

    private bool RowPasses(SessionRowViewModel? row)
    {
        if (row is null) return false;
        if (!MatchesTokens(row)) return false;

        // Offline filter: hide offline rows unless ShowOffline is on, OR we're in the
        // "no online matched, surface offline matches" fallback mode.
        if (row.IsOffline && !ShowOffline && !_showOfflineFallback) return false;
        return true;
    }

    private bool MatchesTokens(SessionRowViewModel row)
    {
        if (_filterTokens.Length == 0) return true;
        var hay = row.SearchHaystack;
        foreach (var tok in _filterTokens)
        {
            if (!hay.Contains(tok, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>Apply an aggregator update on the UI thread.</summary>
    public void ApplyUpdate(IReadOnlyList<SessionState> sessions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sessions)
        {
            seen.Add(s.SessionId);
            if (!_byId.TryGetValue(s.SessionId, out var vm))
            {
                vm = new SessionRowViewModel(s.SessionId, s.SessionDirectory)
                {
                    MuteService = MuteService,
                };
                vm.PropertyChanged += OnRowPropertyChanged;
                _byId[s.SessionId] = vm;
                Sessions.Add(vm);
            }
            vm.UpdateFrom(s, vm.IsExpanded);
        }

        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            var vm = Sessions[i];
            if (!seen.Contains(vm.SessionId))
            {
                vm.PropertyChanged -= OnRowPropertyChanged;
                _byId.Remove(vm.SessionId);
                Sessions.RemoveAt(i);
            }
        }

        WorstStatus = sessions.Where(s => s.DerivedStatus != SessionStatus.Offline)
                              .Select(s => s.DerivedStatus)
                              .DefaultIfEmpty(SessionStatus.Offline)
                              .Max();

        var byStatus = sessions.GroupBy(s => s.DerivedStatus).ToDictionary(g => g.Key, g => g.Count());
        int Get(SessionStatus k) => byStatus.TryGetValue(k, out var c) ? c : 0;

        int active = Get(SessionStatus.Red) + Get(SessionStatus.Blue) + Get(SessionStatus.Yellow) + Get(SessionStatus.Green);
        int offline = Get(SessionStatus.Offline);
        SummaryText = $"{active} active \u00B7 {offline} offline";
        LegendCount = "v0.1 \u00B7 refresh 5s";

        // Stats row: total events parsed across all sessions + oldest live age + total tokens.
        long totalEvents = Core.SessionTailer.TotalEventsProcessed;
        long totalTokens = sessions.Sum(s => s.OutputTokens);
        DateTimeOffset? oldest = null;
        foreach (var s in sessions)
        {
            if (s.DerivedStatus == SessionStatus.Offline) continue;
            if (s.CreatedAt is null) continue;
            if (oldest is null || s.CreatedAt < oldest) oldest = s.CreatedAt;
        }
        var parts = new List<string>
        {
            $"{totalEvents:N0} events parsed",
            $"{totalTokens:N0} output tokens",
        };
        if (oldest is not null)
            parts.Add($"oldest live: {HumanAge(DateTimeOffset.UtcNow - oldest.Value)}");
        StatsText = string.Join(" \u00B7 ", parts);

        SessionsView.Refresh();
        RecomputeFilter();
    }

    private static string HumanAge(TimeSpan d)
    {
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h {d.Minutes}m";
        return $"{(int)d.TotalDays}d {d.Hours}h";
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    [RelayCommand]
    private void ExpandTopVisibleRow()
    {
        foreach (var item in SessionsView)
        {
            if (item is SessionRowViewModel r) { r.IsExpanded = true; return; }
        }
    }

    /// <summary>Find a row by session id and pre-expand it (single-expansion handler
    /// will collapse any other open row). Called by Notifier when a session-scoped
    /// toast fires, so the user finds the row open when they later show the window.</summary>
    public void PreExpand(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        foreach (var r in Sessions)
        {
            if (string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                r.IsExpanded = true;
                return;
            }
        }
    }

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    [RelayCommand]
    private void ToggleShowOffline() => ShowOffline = !ShowOffline;

    [RelayCommand]
    private static void Quit() => System.Windows.Application.Current.Shutdown();

    /// <summary>
    /// Enforce single-expanded-row policy: when one row expands, collapse the
    /// rest. <see cref="_suppressExpansionCascade"/> guards against re-entry
    /// when we set the other rows.
    /// </summary>
    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionRowViewModel.IsExpanded)) return;
        if (_suppressExpansionCascade) return;
        if (sender is not SessionRowViewModel changed || !changed.IsExpanded) return;

        _suppressExpansionCascade = true;
        try
        {
            foreach (var other in Sessions)
            {
                if (!ReferenceEquals(other, changed) && other.IsExpanded)
                    other.IsExpanded = false;
            }
        }
        finally
        {
            _suppressExpansionCascade = false;
        }
    }
}
