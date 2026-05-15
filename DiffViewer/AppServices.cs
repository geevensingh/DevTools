using DiffViewer.Services;

namespace DiffViewer;

/// <summary>
/// App-level services that survive in-place context switches. Constructed
/// once at startup by <see cref="App"/> and passed into every
/// <see cref="CompositionRoot.BuildContextAsync"/> call.
///
/// <para><b>What lives here</b>: services that are stateless across
/// repos, or whose state is intentionally global (e.g. settings file
/// path, app-wide MRU list, resolved external-editor cache).</para>
///
/// <para><b>What does NOT live here</b>: anything tied to a specific
/// repository — those are constructed per-context inside
/// <see cref="CompositionRoot.BuildContextAsync"/> and registered with
/// the per-VM <see cref="Utility.ContextScope"/>.</para>
///
/// <para><b>Late-bound <see cref="ContextSwitcher"/></b>: the coordinator
/// is constructed AFTER this services bundle (it consumes the bundle),
/// so the switcher is assigned post-hoc by <see cref="App.OnStartup"/>.
/// Per-context view-models read this property lazily; tests may leave it
/// null when they don't exercise the dropdown switch path.</para>
/// </summary>
public sealed record AppServices(
    ISettingsService SettingsService,
    IDiffService DiffService,
    IExternalAppLauncher ExternalAppLauncher,
    IRecentContextsService RecentContextsService)
{
    public IContextSwitcher? ContextSwitcher { get; set; }
}
