using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Per-shell view-model that drives the recents <c>ComboBox</c> in
/// <see cref="Views.RecentsBarView"/>. Lifetime is per-context (one
/// instance per <see cref="MainViewModel"/> or
/// <see cref="EmptyContextViewModel"/>); the underlying
/// <see cref="IRecentContextsService"/> is App-level.
///
/// <para><b>SelectedItem two-way binding</b>: WPF assigns
/// <see cref="SelectedItem"/> when the user picks an entry; the setter
/// detects whether the picked entry differs from the currently-active
/// identity and, if so, fires off
/// <see cref="IContextSwitcher.SwitchToRecentAsync"/>. The setter
/// short-circuits when the picked entry equals the current identity,
/// so the binding round-trip after a successful switch (when the new
/// VM's getter naturally returns the just-swapped identity) does not
/// loop.</para>
///
/// <para><b>IsEnabled</b> is bound to <c>!ContextSwitcher.IsSwitching</c>
/// so the dropdown disables itself for the duration of an in-flight
/// switch.</para>
/// </summary>
public sealed class RecentContextsViewModel : ObservableObject, IDisposable
{
    private readonly IRecentContextsService _service;
    private readonly IContextSwitcher? _switcher;
    private readonly ContextIdentity? _currentIdentity;
    private bool _disposed;

    public RecentContextsViewModel(
        IRecentContextsService service,
        IContextSwitcher? switcher,
        ContextIdentity? currentIdentity)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _switcher = switcher;
        _currentIdentity = currentIdentity;

        _service.Changed += OnRecentsChanged;
        if (_switcher is not null)
        {
            _switcher.PropertyChanged += OnSwitcherPropertyChanged;
        }
    }

    /// <summary>MRU-ordered snapshot from the singleton service.</summary>
    public IReadOnlyList<RecentLaunchContext> Items => _service.Current;

    /// <summary>
    /// Projection of <see cref="Items"/> with pre-computed display
    /// strings (Title, Subtitle, Tooltip). The dropdown binds to this
    /// collection rather than to <see cref="Items"/> so the XAML stays
    /// converter-free.
    /// </summary>
    public IReadOnlyList<RecentContextItem> ItemViews => _service.Current
        .Select(c => new RecentContextItem(c))
        .ToList();

    /// <summary>
    /// The entry corresponding to the currently-loaded context, or
    /// <c>null</c> when there is no active context (cold-launch
    /// empty-state) or when the active identity isn't in the list.
    /// </summary>
    public RecentContextItem? SelectedItem
    {
        get
        {
            if (_currentIdentity is not { } id) return null;
            var match = Items.FirstOrDefault(i => Matches(i.Identity, id));
            return match is null ? null : new RecentContextItem(match);
        }
        set
        {
            // WPF calls the setter on user selection. Setter is the
            // ONLY trigger for switching contexts from the dropdown.
            if (value is null) return;
            var picked = value.Source;
            if (_currentIdentity is { } current && Matches(picked.Identity, current))
            {
                // Selecting the already-active item is a no-op (also
                // covers the post-switch binding round-trip).
                return;
            }
            if (_switcher is null)
            {
                // Switcher hasn't been wired (tests, or pre-coordinator
                // bootstrap). Drop the selection on the floor — re-raise
                // SelectedItem so the ComboBox doesn't latch the unhandled
                // pick.
                OnPropertyChanged(nameof(SelectedItem));
                return;
            }

            // Fire-and-forget; the coordinator handles all errors and the
            // post-switch UI rebind.
            _ = SwitchAsync(picked);
        }
    }

    /// <summary>
    /// <c>true</c> when the dropdown is interactive. Bound (negated)
    /// to <see cref="System.Windows.Controls.ComboBox.IsEnabled"/>.
    /// </summary>
    public bool IsEnabled => _switcher is null || !_switcher.IsSwitching;

    /// <summary>True when there are no entries to show — used to surface a hint label.</summary>
    public bool IsEmpty => Items.Count == 0;

    private async Task SwitchAsync(RecentLaunchContext picked)
    {
        try
        {
            await _switcher!.SwitchToRecentAsync(picked).ConfigureAwait(true);
        }
        catch
        {
            // Any thrown exception from the coordinator is already
            // surfaced via the dialog service; we just swallow here so
            // the fire-and-forget Task doesn't crash the dispatcher
            // via UnobservedTaskException.
        }
    }

    private void OnRecentsChanged(object? sender, EventArgs e)
    {
        // The service's Changed event can fire on any thread (the service
        // itself doesn't marshal). WPF requires PropertyChanged on the UI
        // thread for ComboBox bindings; in our flow the service is only
        // ever mutated from the UI thread (App.OnStartup load + coordinator
        // record/remove on the UI scheduler) so this is safe in practice.
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ItemViews));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnSwitcherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IContextSwitcher.IsSwitching))
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    private static bool Matches(ContextIdentity a, ContextIdentity b)
    {
        return ContextIdentityFactory.RepoPathsEqual(a.CanonicalRepoPath, b.CanonicalRepoPath)
            && Equals(a.Left, b.Left)
            && Equals(a.Right, b.Right);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.Changed -= OnRecentsChanged;
        if (_switcher is not null)
        {
            _switcher.PropertyChanged -= OnSwitcherPropertyChanged;
        }
    }
}

/// <summary>
/// Per-row projection of a <see cref="RecentLaunchContext"/> with
/// pre-computed display strings. Equality is by underlying
/// <see cref="ContextIdentity"/> so WPF's <c>SelectedItem</c> matching
/// (which uses <c>Equals</c> against the projected list) round-trips
/// correctly across rebuilds of the projection.
/// </summary>
public sealed class RecentContextItem : IEquatable<RecentContextItem>
{
    public RecentContextItem(RecentLaunchContext source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public RecentLaunchContext Source { get; }

    /// <summary>Primary line. e.g. <c>"DevTools · main → &lt;working-tree&gt;"</c>.</summary>
    public string Title
    {
        get
        {
            var name = SafeBaseName(Source.Identity.CanonicalRepoPath);
            return $"{name} · {LabelFor(Source.LeftDisplay)} → {LabelFor(Source.RightDisplay)}";
        }
    }

    /// <summary>Secondary line. v1: full repo path; relative time deferred to Phase 8.</summary>
    public string Subtitle => Source.Identity.CanonicalRepoPath;

    /// <summary>Tooltip with the full repo path and full ref strings.</summary>
    public string Tooltip
    {
        get
        {
            return $"Repository: {Source.Identity.CanonicalRepoPath}{Environment.NewLine}" +
                   $"Left:  {LabelFor(Source.LeftDisplay)}{Environment.NewLine}" +
                   $"Right: {LabelFor(Source.RightDisplay)}{Environment.NewLine}" +
                   $"Last used: {Source.LastUsedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    private static string LabelFor(DiffSide side) => side switch
    {
        DiffSide.WorkingTree => "<working-tree>",
        DiffSide.CommitIsh c => c.Reference,
        _ => side.ToString() ?? string.Empty,
    };

    private static string SafeBaseName(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath)) return string.Empty;
        try
        {
            var trimmed = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
        catch
        {
            return repoPath;
        }
    }

    public bool Equals(RecentContextItem? other)
        => other is not null && Source.Identity.Equals(other.Source.Identity);

    public override bool Equals(object? obj) => Equals(obj as RecentContextItem);

    public override int GetHashCode() => Source.Identity.GetHashCode();
}
