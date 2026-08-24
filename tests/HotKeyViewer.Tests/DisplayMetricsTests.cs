using System.Globalization;
using HotKeyViewer.Services;
using Xunit;

namespace HotKeyViewer.Tests;

public class DisplayMetricsTests
{
    [Theory]
    // The value gsettings actually prints for a text size of 20px.
    [InlineData("1.6364000000000001", 1.6364)]
    [InlineData("1.0", 1.0)]
    [InlineData("  1.25 \n", 1.25)]
    public void ParsesTheFactorGsettingsPrints(string text, double expected)
    {
        Assert.Equal(expected, DisplayMetrics.ParseScale(text)!.Value, 3);
    }

    [Fact]
    public void ParsesWithAnInvariantDecimalPointRegardlessOfLocale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // A locale using a comma as the decimal separator must not turn
            // 1.6364 into 16364.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(1.6364, DisplayMetrics.ParseScale("1.6364")!.Value, 3);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("uint32 0")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData(null)]
    public void RejectsAnythingThatIsNotAUsableFactor(string? text)
    {
        Assert.Null(DisplayMetrics.ParseScale(text));
    }

    [Theory]
    [InlineData("9", 3.0)]
    [InlineData("0.1", 0.5)]
    public void ClampsAStrayFactorToAUsableRange(string text, double expected)
    {
        Assert.Equal(expected, DisplayMetrics.ParseScale(text)!.Value, 3);
    }

    [Fact]
    public void WindowGrowsWithTheTextScale()
    {
        var plain = new DisplayMetrics(1.0, (4000, 3000)).WindowSize;
        var scaled = new DisplayMetrics(1.5, (4000, 3000)).WindowSize;

        Assert.Equal((1080, 720), plain);
        Assert.Equal((1620, 1080), scaled);
    }

    [Fact]
    public void WindowIsClampedToMostOfTheScreen()
    {
        // This laptop: 3000x2000 at scale 1.6 is 1875x1250 logical.
        var (width, height) = new DisplayMetrics(1.6364, (1875, 1250)).WindowSize;

        Assert.Equal(1688, width);
        Assert.Equal(1125, height);
    }

    [Fact]
    public void WindowNeverShrinksBelowSomethingUsable()
    {
        var (width, height) = new DisplayMetrics(0.5, (320, 240)).WindowSize;

        Assert.Equal(640, width);
        Assert.Equal(400, height);
    }

    [Fact]
    public void UnknownScreenSizeStillSizesFromTheTextScale()
    {
        Assert.Equal((1080, 720), new DisplayMetrics(1.0, null).WindowSize);
    }

    [Fact]
    public void DefaultsToNoScalingWhenNothingIsKnown()
    {
        Assert.Equal(1.0, DisplayMetrics.Default.TextScale);
    }
}
