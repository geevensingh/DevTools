using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Persistent user settings. Implementations are responsible for loading
/// the JSON at startup, surfacing it as an immutable <see cref="AppSettings"/>
/// snapshot, accepting <see cref="Save"/> calls that atomically write back
/// to disk, and raising <see cref="Changed"/> after every successful write
/// so VMs that bind to settings can react.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current settings (immutable snapshot; replaced on every Save).</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Persist <paramref name="updated"/> to <c>%APPDATA%\DiffViewer\settings.json</c>
    /// via temp-file + <c>File.Replace</c> atomic swap, then update
    /// <see cref="Current"/> and raise <see cref="Changed"/>.
    /// </summary>
    void Save(AppSettings updated);

    /// <summary>
    /// Mutate the current settings via the supplied builder. Convenience
    /// over read-modify-Save. Returns the new snapshot.
    /// </summary>
    AppSettings Update(Func<AppSettings, AppSettings> mutate);

    /// <summary>Raised after a successful <see cref="Save"/>.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// True if the most-recent load encountered a corrupt or
    /// future-version JSON file and renamed it to a backup. The UI surfaces
    /// this as a non-modal toast on first show.
    /// </summary>
    SettingsLoadOutcome LastLoadOutcome { get; }
}

public sealed class SettingsChangedEventArgs : EventArgs
{
    public AppSettings Previous { get; }
    public AppSettings Current { get; }
    public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous;
        Current = current;
    }
}

/// <summary>
/// What happened the last time <see cref="ISettingsService"/> loaded the
/// JSON file from disk.
/// </summary>
public enum SettingsLoadOutcome
{
    /// <summary>JSON parsed cleanly and matched the current schema.</summary>
    Loaded,
    /// <summary>No JSON file existed; defaults were used.</summary>
    DefaultsUsed,
    /// <summary>JSON parsed cleanly but had an older schema; migrations ran successfully.</summary>
    Migrated,
    /// <summary>JSON failed to parse; file was backed up and defaults loaded.</summary>
    CorruptBackedUp,
    /// <summary>JSON had a schemaVersion newer than this build; file was backed up and defaults loaded.</summary>
    FutureVersionBackedUp,
}
