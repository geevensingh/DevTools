using System.Windows.Media;
using DiffViewer.Models;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class DiffColorSchemeTests
{
    [Theory]
    [InlineData(ColorSchemePresetName.Classic)]
    [InlineData(ColorSchemePresetName.GitHub)]
    [InlineData(ColorSchemePresetName.HighContrast)]
    [InlineData(ColorSchemePresetName.ColorblindFriendly)]
    [InlineData(ColorSchemePresetName.SolarizedLight)]
    [InlineData(ColorSchemePresetName.Pale)]
    [InlineData(ColorSchemePresetName.Monochrome)]
    public void From_Preset_ReturnsAFullyPopulatedScheme(ColorSchemePresetName name)
    {
        var scheme = DiffColorScheme.From(ColorSchemeChoice.Preset(name));

        scheme.AddedLineBackground.Should().NotBeNull();
        scheme.RemovedLineBackground.Should().NotBeNull();
        scheme.ModifiedLineBackground.Should().NotBeNull();
        scheme.AddedIntraLineBackground.Should().NotBeNull();
        scheme.RemovedIntraLineBackground.Should().NotBeNull();
    }

    [Fact]
    public void From_AllPresets_ProduceDistinctAddedLineColors()
    {
        var distinct = Enum.GetValues<ColorSchemePresetName>()
            .Select(n => ((SolidColorBrush)DiffColorScheme.From(ColorSchemeChoice.Preset(n)).AddedLineBackground).Color)
            .Distinct()
            .Count();

        // Seven presets, seven distinct added-line colors.
        distinct.Should().Be(Enum.GetValues<ColorSchemePresetName>().Length);
    }

    [Fact]
    public void From_Custom_ParsesValidHexColors()
    {
        var scheme = DiffColorScheme.From(ColorSchemeChoice.Custom(
            new ColorSchemeColors(
                AddedLineBg: "#112233",
                RemovedLineBg: "#445566",
                ModifiedLineBg: "#778899",
                AddedIntraline: "#AABBCC",
                RemovedIntraline: "#DDEEFF")));

        ((SolidColorBrush)scheme.AddedLineBackground).Color.Should().Be(Color.FromRgb(0x11, 0x22, 0x33));
        ((SolidColorBrush)scheme.RemovedLineBackground).Color.Should().Be(Color.FromRgb(0x44, 0x55, 0x66));
        ((SolidColorBrush)scheme.ModifiedLineBackground).Color.Should().Be(Color.FromRgb(0x77, 0x88, 0x99));
    }

    [Fact]
    public void From_Custom_FallsBackPerColorOnParseFailure()
    {
        var scheme = DiffColorScheme.From(ColorSchemeChoice.Custom(
            new ColorSchemeColors(
                AddedLineBg: "not-a-color",
                RemovedLineBg: "#ABCDEF",
                ModifiedLineBg: "",
                AddedIntraline: "garbage",
                RemovedIntraline: "#112233")));

        // Bad fields fall through to Classic; good fields parse normally.
        scheme.AddedLineBackground.Should().Be(DiffColorScheme.Classic.AddedLineBackground);
        ((SolidColorBrush)scheme.RemovedLineBackground).Color.Should().Be(Color.FromRgb(0xAB, 0xCD, 0xEF));
        scheme.ModifiedLineBackground.Should().Be(DiffColorScheme.Classic.ModifiedLineBackground);
    }
}
