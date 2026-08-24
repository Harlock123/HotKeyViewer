using HotKeyViewer.Sources;
using Xunit;

namespace HotKeyViewer.Tests;

public class ConfConfigScannerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hotkeyviewer-conf").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string name, string contents) =>
        File.WriteAllText(Path.Combine(_directory, name), contents);

    [Fact]
    public void ReadsBindsExpandsVariablesAndFollowsIncludes()
    {
        Write("hyprland.conf", """
            $mainMod = SUPER
            $mainModShift = SUPER SHIFT

            bind = $mainMod, Q, exec, kitty
            bindd = $mainModShift, B, Open browser, exec, firefox --new-window
            bindm = $mainMod, mouse:272, movewindow

            source = ./extra.conf
            """);

        Write("extra.conf", "bind = ALT, TAB, cyclenext\n");

        var result = ConfConfigScanner.Scan(_directory);

        Assert.Equal(4, result.Binds.Count);

        var launch = result.Binds[0];
        Assert.Equal(64, launch.Chord.ModMask);
        Assert.Equal("Q", launch.Chord.Key);
        Assert.Equal("exec", launch.Kind);
        Assert.Equal("kitty", launch.Command);

        // The `d` flag shifts the description in ahead of the dispatcher.
        var browser = result.Binds[1];
        Assert.Equal("Open browser", browser.Description);
        Assert.Equal("firefox --new-window", browser.Command);
        Assert.Equal(65, browser.Chord.ModMask);

        // A longest-name-first expansion; $mainModShift must not be eaten by $mainMod.
        Assert.Contains(result.Binds, b => b.Chord.Key == "TAB" && b.Chord.ModMask == 8);
    }

    [Fact]
    public void KeepsCommasInsideTheDispatcherArgument()
    {
        Write("hyprland.conf", "bind = SUPER, R, exec, sh -c 'echo a,b,c'\n");

        Assert.Equal("sh -c 'echo a,b,c'", ConfConfigScanner.Scan(_directory).Binds[0].Command);
    }

    [Fact]
    public void RecordsUnbindsAndIgnoresComments()
    {
        Write("hyprland.conf", """
            # bind = SUPER, X, exec, nope
            unbind = SUPER, W
            bind = SUPER, E, exec, thunar # trailing comment
            """);

        var result = ConfConfigScanner.Scan(_directory);

        Assert.Single(result.Binds, b => b.IsUnbind && b.Chord.Key == "W");
        Assert.DoesNotContain(result.Binds, b => b.Chord.Key == "X");
        Assert.Equal("thunar", result.Binds.Single(b => b.Chord.Key == "E").Command);
    }

    [Fact]
    public void SurvivesAnIncludeCycle()
    {
        Write("hyprland.conf", "source = ./loop.conf\nbind = SUPER, A, exec, one\n");
        Write("loop.conf", "source = ./hyprland.conf\nbind = SUPER, B, exec, two\n");

        var result = ConfConfigScanner.Scan(_directory);

        Assert.Equal(2, result.Binds.Count);
    }

    [Fact]
    public void ReportsAMissingInclude()
    {
        Write("hyprland.conf", "source = ./nope.conf\n");

        Assert.Contains(ConfConfigScanner.Scan(_directory).Warnings, w => w.Contains("nope.conf"));
    }

    [Fact]
    public void ReturnsEmptyWhenThereIsNoConfig()
    {
        Assert.Empty(ConfConfigScanner.Scan(Path.Combine(_directory, "absent")).Binds);
    }
}
