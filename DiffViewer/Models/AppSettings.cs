namespace DiffViewer.Models;

/// <summary>
/// All persisted user settings. Loaded once at startup, mutated through
/// <see cref="DiffViewer.Services.ISettingsService"/>, and saved
/// atomically to <c>%APPDATA%\DiffViewer\settings.json</c>.
///
/// <para>Every field has a sensible default so a missing JSON file or a
/// missing field never crashes the app.</para>
///
/// <para><b>Schema versioning:</b> the on-disk JSON carries
/// <see cref="SchemaVersion"/> so we can run migrations when the shape
/// changes. See <see cref="DiffViewer.Services.SettingsService"/> and
/// <see cref="DiffViewer.Services.SettingsMigrations"/>.</para>
/// </summary>
public sealed record AppSettings
{
    /// <summary>Current schema version; bump every time the shape changes.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    // ---- Toolbar toggles (persisted across launches per the plan) ----
    public bool IgnoreWhitespace { get; init; }
    public bool ShowIntraLineDiff { get; init; } = true;
    public bool IsSideBySide { get; init; } = true;
    public bool ShowVisibleWhitespace { get; init; }
    public bool LiveUpdates { get; init; } = true;

    // ---- File-list display mode ----
    public FileListDisplayMode DisplayMode { get; init; } = FileListDisplayMode.RepoRelative;

    // ---- Limits ----
    /// <summary>Files larger than this on either side are skipped (placeholder shown).</summary>
    public long LargeFileThresholdBytes { get; init; } = 25L * 1024 * 1024;

    // ---- Diff-pane appearance ----
    public string FontFamily { get; init; } = "Consolas";
    public double FontSize { get; init; } = 11.0;
    public int TabWidth { get; init; } = 4;
    public bool ShowLineNumbers { get; init; } = true;
    public bool WordWrap { get; init; }
    public ColorSchemeChoice ColorScheme { get; init; } = ColorSchemeChoice.Preset(ColorSchemePresetName.Classic);

    // ---- External editor (auto-detect when null/empty) ----
    public string? ExternalEditorPath { get; init; }
    public string? ExternalEditorLineArgFormat { get; init; }

    // ---- "Don't ask me again" flags for destructive ops ----
    public bool SuppressRevertHunkConfirmation { get; init; }
    public bool SuppressDeleteFileConfirmation { get; init; }
}

/// <summary>
/// Discriminated union: either a named preset or a hand-rolled palette.
/// Persisted as <c>{ "type": "preset", "name": "Classic" }</c> or
/// <c>{ "type": "custom", "colors": { "addedLineBg": "#...", ... } }</c>
/// so the dialog never silently overwrites a hand-edited palette on
/// live-save.
/// </summary>
public abstract record ColorSchemeChoice
{
    public static ColorSchemeChoice Preset(ColorSchemePresetName name) => new PresetScheme(name);
    public static ColorSchemeChoice Custom(ColorSchemeColors colors) => new CustomScheme(colors);

    public sealed record PresetScheme(ColorSchemePresetName Name) : ColorSchemeChoice;
    public sealed record CustomScheme(ColorSchemeColors Colors) : ColorSchemeChoice;
}

/// <summary>The seven presets named in the plan's Diff appearance section.</summary>
public enum ColorSchemePresetName
{
    Classic,
    GitHub,
    HighContrast,
    ColorblindFriendly,
    SolarizedLight,
    Pale,
    Monochrome,
}

/// <summary>Five colors that define a diff palette - hex strings.</summary>
public sealed record ColorSchemeColors(
    string AddedLineBg,
    string RemovedLineBg,
    string ModifiedLineBg,
    string AddedIntraline,
    string RemovedIntraline);
