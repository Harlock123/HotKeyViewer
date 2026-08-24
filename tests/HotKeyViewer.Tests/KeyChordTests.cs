using HotKeyViewer.Models;
using Xunit;

namespace HotKeyViewer.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("SUPER + SHIFT + RETURN", 65, "RETURN")]
    [InlineData("SUPER + code:10", 64, "code:10")]
    [InlineData("CTRL + ALT + DELETE", 12, "DELETE")]
    [InlineData("XF86AudioMute", 0, "XF86AudioMute")]
    public void ParsesModifiersAndKey(string text, int expectedMask, string expectedKey)
    {
        var chord = KeyChord.Parse(text);

        Assert.Equal(expectedMask, chord.ModMask);
        Assert.Equal(expectedKey, chord.Key);
    }

    [Fact]
    public void RepeatedModifierDoesNotDoubleTheBit()
    {
        // Addition rather than a bitwise or would turn SHIFT+SHIFT into CAPS.
        Assert.Equal(1, KeyChord.Parse("SHIFT + SHIFT + A").ModMask);
    }

    [Fact]
    public void DisplaysModifiersInAStableOrder()
    {
        // Written ALT-first, but always shown SUPER-first.
        Assert.Equal("SUPER + ALT + F", KeyChord.Parse("ALT + SUPER + F").Display);
    }

    [Fact]
    public void MatchKeyIgnoresCaseSoSourcesCanBeJoined()
    {
        Assert.Equal(KeyChord.Parse("SUPER + n").MatchKey, KeyChord.Parse("SUPER + N").MatchKey);
    }

    [Fact]
    public void PartsListsEachKeycapSeparately()
    {
        Assert.Equal(["SUPER", "SHIFT", "B"], KeyChord.Parse("SUPER + SHIFT + B").Parts);
    }
}
