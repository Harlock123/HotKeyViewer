using HotKeyViewer.Models;
using HotKeyViewer.Services;
using HotKeyViewer.Sources;
using Xunit;

namespace HotKeyViewer.Tests;

/// <summary>
/// A stock Hyprland install, with no distribution layer: a hyprlang config, real
/// dispatcher names from the compositor, and every binding written by the user.
/// </summary>
public class VanillaHyprlandTests : IDisposable
{
    private readonly string _configDirectory =
        Directory.CreateTempSubdirectory("hotkeyviewer-vanilla").FullName;

    public void Dispose() => Directory.Delete(_configDirectory, recursive: true);

    /// <summary>
    /// What `hyprctl binds` prints on a stock install: a real dispatcher and
    /// argument, unlike the "__lua" every Lua-defined bind reports.
    /// </summary>
    private static string LiveBinds() =>
        "bindd\n\tmodmask: 64\n\tsubmap: \n\tkey: Q\n\tkeycode: 0\n\tcatchall: false\n" +
        "\tdescription: Terminal\n\tdispatcher: exec\n\targ: kitty\n\n" +
        "bindd\n\tmodmask: 64\n\tsubmap: \n\tkey: E\n\tkeycode: 0\n\tcatchall: false\n" +
        "\tdescription: Files\n\tdispatcher: exec\n\targ: thunar\n\n" +
        "bind\n\tmodmask: 64\n\tsubmap: \n\tkey: LEFT\n\tkeycode: 0\n\tcatchall: false\n" +
        "\tdescription: \n\tdispatcher: movefocus\n\targ: l\n";

    private List<HotKey> Build()
    {
        File.WriteAllText(Path.Combine(_configDirectory, "hyprland.conf"), """
            $mainMod = SUPER

            bindd = $mainMod, Q, Terminal, exec, kitty
            bindd = $mainMod, E, Files, exec, thunar
            bind = $mainMod, left, movefocus, l
            """);

        return HotKeyCatalogBuilder.Merge(
            HyprctlBindsReader.Parse(LiveBinds()),
            ConfConfigScanner.Scan(_configDirectory),
            KeycodeResolver.BuiltIn,
            _configDirectory,
            []);
    }

    [Fact]
    public void EveryLiveBindingIsListed()
    {
        Assert.Equal(3, Build().Count);
    }

    [Fact]
    public void CommandsComeThroughEvenWithNoLuaScan()
    {
        // The compositor names the real dispatcher here, so this works with no
        // lua interpreter present at all.
        var terminal = Build().Single(k => k.Description == "Terminal");

        Assert.Equal("kitty", terminal.Command);
    }

    [Fact]
    public void BindingsAreAttributedToTheConfigFileAndLine()
    {
        var files = Build().Single(k => k.Description == "Files");

        Assert.Equal("hyprland.conf", Path.GetFileName(files.SourceFile));
        Assert.Equal(4, files.SourceLine);
    }

    [Fact]
    public void ABindWithNoDescriptionFallsBackToItsDispatcher()
    {
        var focus = Build().Single(k => k.Chord.Key == "LEFT");

        Assert.Equal("movefocus l", focus.Description);
    }

    [Fact]
    public void NothingIsMarkedAsRemappedWhenThereAreNoDefaultsToRemap()
    {
        // Without a distribution layer there is no default to override, so the
        // REMAPPED badge must never appear.
        Assert.All(Build(), hotKey => Assert.False(hotKey.IsOverride));
    }

    [Fact]
    public void CategoriesStayMeaningfulWithoutADistributionLayer()
    {
        // Every binding is the user's own here. Filing them all under "Your
        // bindings" would collapse the whole list into one bucket and make the
        // Yours/Defaults filter meaningless, so the grouping must fall back to
        // something that still separates them.
        var categories = Build().Select(k => k.Category).Distinct().ToList();

        Assert.DoesNotContain("Your bindings", categories);
    }
}
