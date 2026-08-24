using HotKeyViewer.Models;
using HotKeyViewer.Services;
using Xunit;

namespace HotKeyViewer.Tests;

public class BindingRemoverTests : IDisposable
{
    private readonly string _configDirectory =
        Directory.CreateTempSubdirectory("hotkeyviewer-remove").FullName;

    public void Dispose() => Directory.Delete(_configDirectory, recursive: true);

    private HotKey Make(
        string chord,
        string sourceFile,
        int line = 1,
        int shareCount = 1,
        BindOrigin origin = BindOrigin.User)
    {
        var parsed = KeyChord.Parse(chord);

        return new HotKey
        {
            Chord = parsed,
            RawChord = parsed,
            Description = "Something",
            Origin = origin,
            SourceFile = sourceFile,
            SourceLine = line,
            DefinitionShareCount = shareCount,
        };
    }

    [Fact]
    public void YourOwnSingleLineBindingHasItsDefinitionCommentedOut()
    {
        var file = Path.Combine(_configDirectory, "bindings.lua");
        File.WriteAllText(file, "x\n");

        var plan = BindingRemover.Plan(Make("SUPER + I", file), _configDirectory, isLuaConfig: true);

        Assert.Equal(RemovalKind.CommentOut, plan.Kind);
        Assert.Equal(file, plan.TargetFile);
    }

    [Fact]
    public void ADistributionBindingIsOverriddenRatherThanEdited()
    {
        // Editing under /usr/share is pointless: the next update reverts it.
        var plan = BindingRemover.Plan(
            Make("SUPER + SHIFT + B", "/usr/share/omarchy/default/hypr/bindings/applications.lua", origin: BindOrigin.Default),
            _configDirectory,
            isLuaConfig: true);

        Assert.Equal(RemovalKind.Unbind, plan.Kind);
        Assert.Equal("hl.unbind(\"SUPER + SHIFT + B\")", plan.TextToAppend);
        Assert.Contains("distribution", plan.Explanation);
    }

    [Fact]
    public void ALoopGeneratedBindingIsNeverRemovedByDeletingItsLine()
    {
        // Omarchy's `for workspace = 1, 10` puts ten bindings on one line.
        // Commenting it out would remove all ten.
        var file = Path.Combine(_configDirectory, "bindings.lua");
        File.WriteAllText(file, "x\n");

        var plan = BindingRemover.Plan(
            Make("SUPER + code:10", file, shareCount: 10),
            _configDirectory,
            isLuaConfig: true);

        Assert.Equal(RemovalKind.Unbind, plan.Kind);
        Assert.Contains("9 other binding", plan.Explanation);
    }

    [Fact]
    public void AnUnbindNamesTheRawKeyNotTheOneOnScreen()
    {
        // The UI shows "SUPER + 1"; an unbind naming that would not match the
        // keycode binding Hyprland actually holds.
        var hotKey = new HotKey
        {
            Chord = KeyChord.Parse("SUPER + 1"),
            RawChord = KeyChord.Parse("SUPER + code:10"),
            Description = "Switch to workspace 1",
            Origin = BindOrigin.Default,
            SourceFile = "/usr/share/omarchy/default/hypr/bindings/tiling.lua",
            SourceLine = 22,
            DefinitionShareCount = 10,
        };

        var plan = BindingRemover.Plan(hotKey, _configDirectory, isLuaConfig: true);

        Assert.Equal("hl.unbind(\"SUPER + code:10\")", plan.TextToAppend);
    }

    [Fact]
    public void AConfConfigGetsHyprlangSyntax()
    {
        var plan = BindingRemover.Plan(
            Make("SUPER + SHIFT + B", "/etc/hypr/hyprland.conf", origin: BindOrigin.Default),
            _configDirectory,
            isLuaConfig: false);

        Assert.Equal("unbind = SUPER SHIFT, B", plan.TextToAppend);
        Assert.EndsWith("hyprland.conf", plan.TargetFile);
    }

    [Fact]
    public void OverridesGoToBindingsLuaWhenItExists()
    {
        var bindings = Path.Combine(_configDirectory, "bindings.lua");
        File.WriteAllText(bindings, "");

        Assert.Equal(bindings, BindingRemover.OverrideFile(_configDirectory, isLuaConfig: true));
    }

    [Fact]
    public void OverridesFallBackToTheEntryPointWhenThereIsNoBindingsFile()
    {
        Assert.EndsWith("hyprland.lua", BindingRemover.OverrideFile(_configDirectory, isLuaConfig: true));
    }

    [Fact]
    public void ABindingWithNoNameableChordIsRefused()
    {
        var chord = new KeyChord(64, string.Empty);

        var plan = BindingRemover.Plan(
            new HotKey
            {
                Chord = chord,
                RawChord = chord,
                Description = "Mystery",
                DefinitionShareCount = 1,
            },
            _configDirectory,
            isLuaConfig: true);

        Assert.Equal(RemovalKind.Unsupported, plan.Kind);
        Assert.False(plan.CanApply);
    }

    [Theory]
    [InlineData("bindings.lua", "-- ")]
    [InlineData("hyprland.conf", "# ")]
    public void CommentSyntaxFollowsTheFileType(string file, string expected)
    {
        Assert.Equal(expected, BindingRemover.CommentPrefix(file));
    }
}
