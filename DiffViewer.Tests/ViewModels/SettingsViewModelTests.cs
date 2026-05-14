using System;
using System.IO;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly SettingsService _service;

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DiffViewer.SettingsVmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
        _service = new SettingsService(_settingsPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private SettingsViewModel NewVm() => new(_service, useDispatcherTimer: false);

    [Fact]
    public void Constructor_LoadsCurrentSettings()
    {
        _service.Save(_service.Current with { FontFamily = "Cascadia Code", TabWidth = 2 });
        var vm = NewVm();

        vm.FontFamily.Should().Be("Cascadia Code");
        vm.TabWidth.Should().Be(2);
    }

    [Fact]
    public void Constructor_DoesNotPersistDuringSeed()
    {
        // First construct the file with a non-default value.
        _service.Save(_service.Current with { FontFamily = "Cascadia Code" });
        var bytesBefore = File.ReadAllBytes(_settingsPath);

        // Constructing a VM should NOT rewrite the file just because it
        // pumped the seed values through its observable properties.
        _ = NewVm();

        File.ReadAllBytes(_settingsPath).Should().Equal(bytesBefore);
    }

    [Fact]
    public void Toggle_ShowLineNumbers_PersistsImmediately()
    {
        var vm = NewVm();
        vm.ShowLineNumbers = false;

        new SettingsService(_settingsPath).Current.ShowLineNumbers.Should().BeFalse();
    }

    [Fact]
    public void Toggle_WordWrap_PersistsImmediately()
    {
        var vm = NewVm();
        vm.WordWrap = true;

        new SettingsService(_settingsPath).Current.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void ConfirmRevertHunk_PersistsAsInvertedSuppressFlag()
    {
        var vm = NewVm();
        vm.ConfirmRevertHunk = false;

        new SettingsService(_settingsPath).Current.SuppressRevertHunkConfirmation.Should().BeTrue();
    }

    [Fact]
    public void ConfirmDeleteFile_PersistsAsInvertedSuppressFlag()
    {
        var vm = NewVm();
        vm.ConfirmDeleteFile = false;

        new SettingsService(_settingsPath).Current.SuppressDeleteFileConfirmation.Should().BeTrue();
    }

    [Fact]
    public void NumericInputs_DoNotPersistUntilCommit()
    {
        var vm = NewVm();
        vm.FontSize = 16.0;

        // Until CommitNumericFields() runs, the file still has the default value.
        new SettingsService(_settingsPath).Current.FontSize.Should().Be(11.0);

        vm.CommitNumericFields();
        new SettingsService(_settingsPath).Current.FontSize.Should().Be(16.0);
    }

    [Fact]
    public void TextInputs_DoNotPersistUntilCommit()
    {
        var vm = NewVm();
        vm.ExternalEditorPath = @"C:\bin\code.cmd";

        new SettingsService(_settingsPath).Current.ExternalEditorPath.Should().BeNull();

        vm.CommitNumericFields();
        new SettingsService(_settingsPath).Current.ExternalEditorPath.Should().Be(@"C:\bin\code.cmd");
    }

    [Fact]
    public void CommitNumericFields_ClampsOutOfRange()
    {
        var vm = NewVm();
        vm.FontSize = 999;
        vm.TabWidth = 0;
        vm.LargeFileThresholdMb = 0;

        vm.CommitNumericFields();

        var saved = new SettingsService(_settingsPath).Current;
        saved.FontSize.Should().Be(72.0);
        saved.TabWidth.Should().Be(1);
        saved.LargeFileThresholdBytes.Should().Be(1L * 1024 * 1024);
    }

    [Fact]
    public void CommitNumericFields_NormalizesEmptyTextToNull()
    {
        _service.Save(_service.Current with { ExternalEditorPath = "old", ExternalEditorLineArgFormat = "old" });
        var vm = NewVm();
        vm.ExternalEditorPath = "";
        vm.ExternalEditorLineArgFormat = "   ";

        vm.CommitNumericFields();

        var saved = new SettingsService(_settingsPath).Current;
        saved.ExternalEditorPath.Should().BeNull();
        saved.ExternalEditorLineArgFormat.Should().BeNull();
    }

    [Fact]
    public void ColorPreset_PersistsImmediatelyWhenNoDispatcher()
    {
        var vm = NewVm();
        vm.SelectedColorPreset = ColorSchemePresetName.HighContrast;

        var saved = new SettingsService(_settingsPath).Current.ColorScheme;
        saved.Should().BeOfType<ColorSchemeChoice.PresetScheme>()
            .Which.Name.Should().Be(ColorSchemePresetName.HighContrast);
    }

    [Fact]
    public void ColorPreset_PickingPresetClearsCustomFlag()
    {
        // Seed a custom palette as if someone hand-edited the JSON.
        var custom = new ColorSchemeColors("#aaa", "#bbb", "#ccc", "#ddd", "#eee");
        _service.Save(_service.Current with { ColorScheme = ColorSchemeChoice.Custom(custom) });

        var vm = NewVm();
        vm.IsCustomColorScheme.Should().BeTrue();

        vm.SelectedColorPreset = ColorSchemePresetName.Pale;
        vm.IsCustomColorScheme.Should().BeFalse();

        var saved = new SettingsService(_settingsPath).Current.ColorScheme;
        saved.Should().BeOfType<ColorSchemeChoice.PresetScheme>()
            .Which.Name.Should().Be(ColorSchemePresetName.Pale);
    }

    [Fact]
    public void Constructor_FlagsCustomColorScheme()
    {
        var custom = new ColorSchemeColors("#aaa", "#bbb", "#ccc", "#ddd", "#eee");
        _service.Save(_service.Current with { ColorScheme = ColorSchemeChoice.Custom(custom) });

        var vm = NewVm();
        vm.IsCustomColorScheme.Should().BeTrue();
    }

    [Fact]
    public void ResetAllToDefaults_RewritesFileAndReloadsState()
    {
        _service.Save(_service.Current with
        {
            FontFamily = "Cascadia Code",
            FontSize = 18,
            TabWidth = 8,
            LargeFileThresholdBytes = 99L * 1024 * 1024,
        });

        // Confirm prompt accepts.
        var vm = new SettingsViewModel(_service, confirmReset: _ => true, useDispatcherTimer: false);
        vm.ResetAllToDefaultsCommand.Execute(null);

        var saved = new SettingsService(_settingsPath).Current;
        saved.Should().BeEquivalentTo(new AppSettings());
        vm.FontFamily.Should().Be("Consolas");
        vm.LargeFileThresholdMb.Should().Be(25);
    }

    [Fact]
    public void ResetAllToDefaults_RespectsConfirmCancel()
    {
        _service.Save(_service.Current with { FontFamily = "Cascadia Code" });

        var vm = new SettingsViewModel(_service, confirmReset: _ => false, useDispatcherTimer: false);
        vm.ResetAllToDefaultsCommand.Execute(null);

        new SettingsService(_settingsPath).Current.FontFamily.Should().Be("Cascadia Code");
    }

    [Fact]
    public void OpenSettingsJson_UsesInjectedHandlerWhenProvided()
    {
        string? opened = null;
        var vm = new SettingsViewModel(_service, openInEditor: p => opened = p, useDispatcherTimer: false);

        vm.OpenSettingsJsonCommand.Execute(null);

        opened.Should().Be(SettingsService.DefaultFilePath);
    }

    [Fact]
    public void AvailableFonts_DefaultsToEmpty_WhenNotInjected()
    {
        var vm = NewVm();
        vm.AvailableFonts.Should().NotBeNull();
        vm.AvailableFonts.Should().BeEmpty();
    }

    [Fact]
    public void AvailableFonts_ReflectsInjectedList()
    {
        var fonts = new[]
        {
            new FontFamilyOption("Cascadia Code", IsMonospaced: true),
            new FontFamilyOption("Segoe UI", IsMonospaced: false),
        };
        var vm = new SettingsViewModel(_service, useDispatcherTimer: false, availableFonts: fonts);

        vm.AvailableFonts.Should().BeEquivalentTo(fonts);
    }

    [Fact]
    public void FontFamilyOption_GroupName_MonospacedAndVariable()
    {
        new FontFamilyOption("Consolas", IsMonospaced: true).GroupName.Should().Be("Monospaced");
        new FontFamilyOption("Arial", IsMonospaced: false).GroupName.Should().Be("Variable width");
    }
}
