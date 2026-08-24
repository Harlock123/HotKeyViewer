using HotKeyViewer.Models;
using HotKeyViewer.Services;
using HotKeyViewer.Sources;
using Xunit;

namespace HotKeyViewer.Tests;

public class DuplicateDetectionTests
{
    private static string Bind(int modmask, string key, string description, string dispatcher, string arg) =>
        $"bindd\n\tmodmask: {modmask}\n\tsubmap: \n\tkey: {key}\n\tkeycode: 0\n\tcatchall: false\n" +
        $"\tdescription: {description}\n\tdispatcher: {dispatcher}\n\targ: {arg}\n\n";

    private static List<HotKey> Build(string binds) =>
        HotKeyCatalogBuilder.Merge(
            HyprctlBindsReader.Parse(binds),
            ConfigScanResult.Empty,
            KeycodeResolver.BuiltIn,
            "/nonexistent",
            []);

    [Fact]
    public void TwoChordsRunningTheSameCommandAreBothFlagged()
    {
        // The real case: Browser is on SUPER+SHIFT+B and SUPER+SHIFT+RETURN.
        var hotKeys = Build(
            Bind(65, "B", "Browser", "exec", "omarchy-launch-browser") +
            Bind(65, "RETURN", "Browser", "exec", "omarchy-launch-browser"));

        Assert.All(hotKeys, hotKey => Assert.Equal(2, hotKey.DuplicateCount));
        Assert.All(hotKeys, hotKey => Assert.True(hotKey.HasDuplicates));
    }

    [Fact]
    public void ADifferentArgumentIsNotADuplicate()
    {
        // Private browsing runs the same binary with a different flag, so it is
        // a different action and must not be offered up for removal.
        var hotKeys = Build(
            Bind(65, "B", "Browser", "exec", "omarchy-launch-browser") +
            Bind(73, "B", "Browser (private)", "exec", "omarchy-launch-browser --private"));

        Assert.All(hotKeys, hotKey => Assert.False(hotKey.HasDuplicates));
    }

    [Fact]
    public void LuaClosuresAreNeverTreatedAsDuplicatesOfEachOther()
    {
        // Every Lua closure renders as "<lua function>" because there is no
        // recoverable text. Grouping on that would call unrelated bindings
        // duplicates of each other.
        var hotKeys = Build(
            Bind(64, "A", "One thing", "__lua", "1") +
            Bind(64, "B", "Another thing", "__lua", "2") +
            Bind(64, "C", "A third thing", "__lua", "3"));

        Assert.All(hotKeys, hotKey => Assert.False(hotKey.HasDuplicates));
    }

    [Fact]
    public void ThreeChordsReportThree()
    {
        var hotKeys = Build(
            Bind(64, "A", "Menu", "exec", "omarchy-menu") +
            Bind(64, "B", "Menu", "exec", "omarchy-menu") +
            Bind(64, "C", "Menu", "exec", "omarchy-menu"));

        Assert.All(hotKeys, hotKey => Assert.Equal("3 KEYS", hotKey.DuplicateLabel));
    }

    [Fact]
    public void AUniqueBindingReportsOneAndCarriesNoBadge()
    {
        var hotKey = Build(Bind(64, "Q", "Close", "exec", "kill")).Single();

        Assert.Equal(1, hotKey.DuplicateCount);
        Assert.False(hotKey.HasDuplicates);
    }
}
