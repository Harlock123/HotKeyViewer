using Avalonia.Media;
using HotKeyViewer.Services;
using Xunit;

namespace HotKeyViewer.Tests;

public class OmarchyThemeTests
{
    // Trimmed from the moodpeak theme's colors.toml.
    private const string Sample = """
        accent = "#4ecdc4"
        cursor = "#e6dbf5"

        foreground = "#e0e6ed"
        background = "#181c22"

        color0 = "#181c22"
        color1 = "#ff7b92"
        color2 = "#4ecdc4"
        color3 = "#82eeff"
        color4 = "#6a85ff"
        """;

    [Fact]
    public void ReadsTheAnchorColours()
    {
        var palette = OmarchyTheme.Parse(Sample);

        Assert.Equal(Color.FromRgb(0x18, 0x1c, 0x22), palette.Background);
        Assert.Equal(Color.FromRgb(0xe0, 0xe6, 0xed), palette.Foreground);
        Assert.Equal(Color.FromRgb(0x4e, 0xcd, 0xc4), palette.Accent);
    }

    [Fact]
    public void ReadsTheAnsiSlotsInOrder()
    {
        var palette = OmarchyTheme.Parse(Sample);

        Assert.Equal(5, palette.Ansi.Count);
        Assert.Equal(Color.FromRgb(0xff, 0x7b, 0x92), palette.Ansi[1]);
    }

    [Fact]
    public void FallsBackToTheBlueAnsiSlotWhenNoAccentIsSet()
    {
        // Not every theme defines an accent; color4 is the conventional stand-in.
        var palette = OmarchyTheme.Parse("""
            background = "#000000"
            foreground = "#ffffff"
            color4 = "#6a85ff"
            """);

        Assert.Equal(Color.FromRgb(0x6a, 0x85, 0xff), palette.Accent);
    }

    [Fact]
    public void DetectsALightTheme()
    {
        // catppuccin-latte.
        var light = OmarchyTheme.Parse("""
            background = "#eff1f5"
            foreground = "#4c4f69"
            accent = "#1e66f5"
            """);

        Assert.True(light.IsLight);
        Assert.False(OmarchyTheme.Parse(Sample).IsLight);
    }

    [Fact]
    public void MixedTonesMoveAwayFromTheBackgroundInBothDirections()
    {
        var dark = OmarchyTheme.Parse(Sample);
        var light = OmarchyTheme.Parse("""
            background = "#eff1f5"
            foreground = "#4c4f69"
            """);

        // On a dark theme an intermediate tone is lighter than the background;
        // on a light theme it must be darker. Same call, opposite direction.
        Assert.True(dark.Mix(0.2).R > dark.Background.R);
        Assert.True(light.Mix(0.2).R < light.Background.R);
    }

    [Fact]
    public void IgnoresCommentsSectionsAndNonColourValues()
    {
        var palette = OmarchyTheme.Parse("""
            # a comment
            [section]
            name = "not a colour"
            background = "#123456"
            foreground = "#abcdef"
            """);

        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), palette.Background);
    }

    [Theory]
    [InlineData("#ff8800", 0xff, 0x88, 0x00)]
    [InlineData("ff8800", 0xff, 0x88, 0x00)]
    public void ParsesHexWithOrWithoutTheHash(string text, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromRgb(r, g, b), OmarchyTheme.ParseHex(text)!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    public void RejectsAnythingThatIsNotHex(string text)
    {
        Assert.Null(OmarchyTheme.ParseHex(text));
    }

    [Fact]
    public void UsesTheBuiltInPaletteWhenTheThemeDefinesNothing()
    {
        var palette = OmarchyTheme.Parse(string.Empty);

        Assert.Equal(ThemePalette.Fallback.Background, palette.Background);
        Assert.Equal(ThemePalette.Fallback.Foreground, palette.Foreground);
    }
}
