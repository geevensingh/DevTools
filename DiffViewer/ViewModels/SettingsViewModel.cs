using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs <c>SettingsDialog.xaml</c>. Reads / writes through
/// <see cref="ISettingsService"/> with the live-save commit policy
/// described in the plan:
/// <list type="bullet">
///   <item>Toggles (checkboxes, dropdowns) → save immediately.</item>
///   <item>Numeric inputs → save on focus-loss / Enter (the View
///     is responsible for triggering <see cref="CommitNumericFields"/>
///     at those moments; the VM never auto-commits per keystroke).</item>
///   <item>Text inputs (editor path / line-arg format) → save on
///     focus-loss / Enter.</item>
///   <item>Color-scheme dropdown → 200 ms trailing-edge debounce.</item>
/// </list>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public const int ColorSchemeDebounceMs = 200;

    private readonly ISettingsService _settings;
    private readonly Action<string>? _openInEditor;
    private readonly Func<string, bool>? _confirmReset;
    private readonly DispatcherTimer? _colorSchemeDebounce;
    private bool _suppress;

    public SettingsViewModel(
        ISettingsService settings,
        Action<string>? openInEditor = null,
        Func<string, bool>? confirmReset = null,
        bool useDispatcherTimer = true)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _openInEditor = openInEditor;
        _confirmReset = confirmReset;

        ColorSchemePresets = new ObservableCollection<ColorSchemePresetName>(
            Enum.GetValues<ColorSchemePresetName>());

        if (useDispatcherTimer && System.Windows.Application.Current is not null)
        {
            _colorSchemeDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ColorSchemeDebounceMs),
            };
            _colorSchemeDebounce.Tick += (_, _) =>
            {
                _colorSchemeDebounce!.Stop();
                CommitColorScheme();
            };
        }

        LoadFromSettings();
    }

    // ---------- Bindable state ----------

    public ObservableCollection<ColorSchemePresetName> ColorSchemePresets { get; }

    // Diff appearance
    [ObservableProperty] private string _fontFamily = "Consolas";
    [ObservableProperty] private double _fontSize = 11.0;
    [ObservableProperty] private int _tabWidth = 4;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private bool _wordWrap;
    [ObservableProperty] private bool _highlightCurrentLine;
    [ObservableProperty] private ColorSchemePresetName _selectedColorPreset = ColorSchemePresetName.Classic;

    /// <summary>True iff the persisted color-scheme is a hand-edited custom palette.</summary>
    [ObservableProperty] private bool _isCustomColorScheme;

    // External editor
    [ObservableProperty] private string _externalEditorPath = string.Empty;
    [ObservableProperty] private string _externalEditorLineArgFormat = string.Empty;

    // Limits
    [ObservableProperty] private int _largeFileThresholdMb = 25;

    // Confirmations (note: bound as positives even though stored as suppress-flags)
    [ObservableProperty] private bool _confirmRevertHunk = true;
    [ObservableProperty] private bool _confirmDeleteFile = true;

    // Status line
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ---------- Commands ----------

    [RelayCommand]
    private void OpenSettingsJson()
    {
        var path = SettingsService.DefaultFilePath;
        try
        {
            if (_openInEditor is not null)
            {
                _openInEditor(path);
                StatusMessage = $"Opened {path}";
                return;
            }

            // OS-default shell-open fallback (per the plan).
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            StatusMessage = $"Opened {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open {path}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetAllToDefaults()
    {
        var path = SettingsService.DefaultFilePath;
        if (_confirmReset is not null && !_confirmReset(
            "Reset all DiffViewer settings to defaults? This cannot be undone."))
        {
            return;
        }

        _settings.Save(new AppSettings());
        LoadFromSettings();
        StatusMessage = "Settings reset to defaults.";
    }

    /// <summary>
    /// Called by the View when a numeric or text input loses focus or the
    /// user presses Enter. The VM holds the buffered value in its
    /// observable property; this method commits it to the settings file.
    /// </summary>
    public void CommitNumericFields()
    {
        if (_suppress) return;
        var clampedFontSize = Math.Clamp(FontSize, 6.0, 72.0);
        var clampedTabWidth = Math.Clamp(TabWidth, 1, 16);
        var clampedThresholdMb = Math.Clamp(LargeFileThresholdMb, 1, 2048);

        if (clampedFontSize != FontSize) FontSize = clampedFontSize;
        if (clampedTabWidth != TabWidth) TabWidth = clampedTabWidth;
        if (clampedThresholdMb != LargeFileThresholdMb) LargeFileThresholdMb = clampedThresholdMb;

        _settings.Update(s => s with
        {
            FontSize = clampedFontSize,
            TabWidth = clampedTabWidth,
            LargeFileThresholdBytes = (long)clampedThresholdMb * 1024 * 1024,
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Consolas" : FontFamily,
            ExternalEditorPath = string.IsNullOrWhiteSpace(ExternalEditorPath) ? null : ExternalEditorPath,
            ExternalEditorLineArgFormat = string.IsNullOrWhiteSpace(ExternalEditorLineArgFormat)
                ? null
                : ExternalEditorLineArgFormat,
        });
        StatusMessage = "Saved.";
    }

    /// <summary>
    /// Synchronously fires any pending color-scheme debounce. The View
    /// calls this from the Close button's handler so the latest preset
    /// click is applied without waiting for the 200 ms window.
    /// </summary>
    public void FlushPendingWrites()
    {
        if (_colorSchemeDebounce is { IsEnabled: true })
        {
            _colorSchemeDebounce.Stop();
            CommitColorScheme();
        }
    }

    // ---------- Toggle persistence (live save) ----------

    partial void OnShowLineNumbersChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { ShowLineNumbers = value });

    partial void OnWordWrapChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { WordWrap = value });

    partial void OnHighlightCurrentLineChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { HighlightCurrentLine = value });

    partial void OnConfirmRevertHunkChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { SuppressRevertHunkConfirmation = !value });

    partial void OnConfirmDeleteFileChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { SuppressDeleteFileConfirmation = !value });

    partial void OnSelectedColorPresetChanged(ColorSchemePresetName value)
    {
        if (_suppress) return;

        // User explicitly picked a preset → opt out of any custom palette.
        IsCustomColorScheme = false;

        if (_colorSchemeDebounce is null)
        {
            // Test path or no Application.Current — commit synchronously.
            CommitColorScheme();
            return;
        }

        _colorSchemeDebounce.Stop();
        _colorSchemeDebounce.Start();
    }

    private void CommitColorScheme()
    {
        if (_suppress) return;
        var preset = SelectedColorPreset;
        _settings.Update(s => s with { ColorScheme = ColorSchemeChoice.Preset(preset) });
        StatusMessage = $"Color scheme: {preset}.";
    }

    private void SaveIfNotSuppressed(Func<AppSettings, AppSettings> mutate)
    {
        if (_suppress) return;
        _settings.Update(mutate);
        StatusMessage = "Saved.";
    }

    private void LoadFromSettings()
    {
        _suppress = true;
        try
        {
            var s = _settings.Current;
            FontFamily = s.FontFamily;
            FontSize = s.FontSize;
            TabWidth = s.TabWidth;
            ShowLineNumbers = s.ShowLineNumbers;
            WordWrap = s.WordWrap;
            HighlightCurrentLine = s.HighlightCurrentLine;
            ExternalEditorPath = s.ExternalEditorPath ?? string.Empty;
            ExternalEditorLineArgFormat = s.ExternalEditorLineArgFormat ?? string.Empty;
            LargeFileThresholdMb = (int)Math.Clamp(s.LargeFileThresholdBytes / (1024 * 1024), 1, 2048);
            ConfirmRevertHunk = !s.SuppressRevertHunkConfirmation;
            ConfirmDeleteFile = !s.SuppressDeleteFileConfirmation;

            switch (s.ColorScheme)
            {
                case ColorSchemeChoice.PresetScheme p:
                    SelectedColorPreset = p.Name;
                    IsCustomColorScheme = false;
                    break;
                case ColorSchemeChoice.CustomScheme:
                    IsCustomColorScheme = true;
                    // Leave SelectedColorPreset at whatever it was (the dialog
                    // shows "Custom (edit JSON)" instead of the dropdown value).
                    break;
            }
        }
        finally { _suppress = false; }
    }

    public void Dispose()
    {
        _colorSchemeDebounce?.Stop();
    }
}
