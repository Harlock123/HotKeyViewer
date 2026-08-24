using HotKeyViewer.Models;
using HotKeyViewer.Sources;
using Xunit;

namespace HotKeyViewer.Tests;

public class HyprctlBindsReaderTests
{
    // Verbatim shape of `hyprctl binds` on Hyprland 0.56.
    private const string Sample =
        "bindled\n" +
        "\tmodmask: 0\n" +
        "\tsubmap: \n" +
        "\tkey: XF86AudioRaiseVolume\n" +
        "\tkeycode: 0\n" +
        "\tcatchall: false\n" +
        "\tdescription: Volume up\n" +
        "\tdispatcher: __lua\n" +
        "\targ: 6\n" +
        "\n" +
        "bindd\n" +
        "\tmodmask: 64\n" +
        "\tsubmap: \n" +
        "\tkey: SUPER + code:10\n" +
        "\tkeycode: 0\n" +
        "\tcatchall: false\n" +
        "\tdescription: Switch to workspace 1\n" +
        "\tdispatcher: __lua\n" +
        "\targ: 23\n";

    [Fact]
    public void ReadsEveryRecord()
    {
        Assert.Equal(2, HyprctlBindsReader.Parse(Sample).Count);
    }

    [Fact]
    public void KeepsOnlyTheKeyFromTheDisplayForm()
    {
        // The text form repeats the modifiers in the key field; they are already
        // carried numerically, so only the tail is the key.
        var bind = HyprctlBindsReader.Parse(Sample)[1];

        Assert.Equal(64, bind.Chord.ModMask);
        Assert.Equal("code:10", bind.Chord.Key);
    }

    [Fact]
    public void DecodesTheFlagLettersOnTheHeader()
    {
        var bind = HyprctlBindsReader.Parse(Sample)[0];

        Assert.True(bind.Options.HasFlag(BindOptions.Locked));
        Assert.True(bind.Options.HasFlag(BindOptions.Repeats));
        Assert.True(bind.Options.HasFlag(BindOptions.HasDescription));
        Assert.False(bind.Options.HasFlag(BindOptions.Mouse));
    }

    [Fact]
    public void FallsBackToTheNumericKeycodeWhenTheKeyIsBlank()
    {
        // Older builds report the number instead of the key text.
        const string olderFormat =
            "bindd\n\tmodmask: 64\n\tkey: \n\tkeycode: 20\n\tdescription: Resize\n\tdispatcher: __lua\n\targ: 1\n";

        Assert.Equal("code:20", HyprctlBindsReader.Parse(olderFormat)[0].Chord.Key);
    }

    [Fact]
    public void ReturnsNothingForEmptyOutput()
    {
        Assert.Empty(HyprctlBindsReader.Parse(string.Empty));
    }
}
