using HotKeyViewer.Models;
using HotKeyViewer.Services;
using HotKeyViewer.ViewModels;
using Xunit;

namespace HotKeyViewer.Tests;

public class MainViewModelTests
{
    private static HotKey Make(string chord, string description, BindOrigin origin, bool isOverride = false, string command = "")
    {
        var parsed = KeyChord.Parse(chord);

        return new HotKey
        {
            Chord = parsed,
            Description = description,
            Command = command,
            Origin = origin,
            IsOverride = isOverride,
            Category = origin == BindOrigin.User ? "Your bindings" : "Applications",
            SearchText = $"{parsed.Display} {description} {command}".ToLowerInvariant(),
        };
    }

    private static MainViewModel Loaded()
    {
        var viewModel = new MainViewModel();

        viewModel.Load(new HotKeyCatalog(
            [
                Make("SUPER + RETURN", "Terminal", BindOrigin.Default, command: "ghostty"),
                Make("SUPER + SHIFT + B", "Browser", BindOrigin.Default, command: "firefox"),
                Make("SUPER + I", "Toggle Toolbox", BindOrigin.User, command: "toolbox-toggle"),
                Make("SUPER + SPACE", "Omarchy menu", BindOrigin.User, isOverride: true),
            ],
            ["hyprland.lua"],
            []) { HasDefaultsLayer = true });

        return viewModel;
    }

    [Fact]
    public void ShowsEveryBindingByDefault()
    {
        Assert.Equal(4, Loaded().Groups.Sum(g => g.HotKeys.Count));
    }

    [Fact]
    public void CustomisedFilterKeepsOnlyUserAndRemappedBindings()
    {
        var viewModel = Loaded();
        viewModel.Filter = FilterMode.Customised;

        var shown = viewModel.Groups.SelectMany(g => g.HotKeys).ToList();

        Assert.Equal(2, shown.Count);
        Assert.All(shown, hotKey => Assert.True(hotKey.IsCustom));
    }

    [Fact]
    public void DefaultsFilterExcludesAnythingTheUserTouched()
    {
        var viewModel = Loaded();
        viewModel.Filter = FilterMode.Defaults;

        Assert.All(viewModel.Groups.SelectMany(g => g.HotKeys), hotKey => Assert.False(hotKey.IsCustom));
    }

    [Fact]
    public void ARemappedDefaultCountsAsCustom()
    {
        // The user replaced SUPER+SPACE, so it is theirs even though the chord
        // came from the defaults.
        Assert.Equal(2, Loaded().CustomCount);
    }

    [Fact]
    public void SearchMatchesTheKeyTheActionOrTheCommand()
    {
        var viewModel = Loaded();

        viewModel.Query = "firefox";
        Assert.Equal("Browser", viewModel.Groups.Single().HotKeys.Single().Description);

        viewModel.Query = "toolbox";
        Assert.Equal("Toggle Toolbox", viewModel.Groups.Single().HotKeys.Single().Description);
    }

    [Fact]
    public void EveryTermMustMatchSoSearchNarrows()
    {
        var viewModel = Loaded();

        viewModel.Query = "super terminal";
        Assert.Single(viewModel.Groups.SelectMany(g => g.HotKeys));

        viewModel.Query = "super nonsense";
        Assert.Empty(viewModel.Groups);
        Assert.True(viewModel.HasNoResults);
    }

    [Fact]
    public void SearchAndFilterCombine()
    {
        var viewModel = Loaded();
        viewModel.Filter = FilterMode.Defaults;
        viewModel.Query = "toolbox";

        // The only "toolbox" binding is the user's, which the filter excludes.
        Assert.Empty(viewModel.Groups);
    }

    [Fact]
    public void OriginFilterIsHiddenWhenThereIsNoDistributionLayer()
    {
        // A stock Hyprland install: nothing came from outside the user's config,
        // so Yours/Defaults would be a choice between everything and nothing.
        var viewModel = new MainViewModel();

        viewModel.Load(new HotKeyCatalog(
            [Make("SUPER + Q", "Terminal", BindOrigin.User, command: "kitty")],
            ["hyprland.conf"],
            []) { HasDefaultsLayer = false });

        Assert.False(viewModel.ShowsOriginFilter);
        Assert.DoesNotContain("yours", viewModel.StatusText);
    }

    [Fact]
    public void OriginFilterIsShownWhenDefaultsExist()
    {
        var viewModel = Loaded();

        Assert.True(viewModel.ShowsOriginFilter);
        Assert.Contains("yours", viewModel.StatusText);
    }

    [Fact]
    public void SectionsStartExpanded()
    {
        Assert.All(Loaded().Groups, group => Assert.True(group.IsExpanded));
    }

    [Fact]
    public void TogglingASectionFlipsIt()
    {
        var viewModel = Loaded();
        var group = viewModel.Groups[0];

        viewModel.ToggleGroup(group);

        Assert.False(group.IsExpanded);
        Assert.True(group.IsCollapsed);
    }

    [Fact]
    public void ACollapsedSectionStaysCollapsedWhenTheListIsRebuilt()
    {
        var viewModel = Loaded();
        var name = viewModel.Groups[0].Name;

        viewModel.ToggleGroup(viewModel.Groups[0]);

        // Changing the filter rebuilds every group from scratch; the choice is
        // remembered by category name rather than by object.
        viewModel.Filter = FilterMode.All;

        Assert.False(viewModel.Groups.Single(g => g.Name == name).IsExpanded);
    }

    [Fact]
    public void SearchingForcesEverySectionOpen()
    {
        var viewModel = Loaded();
        viewModel.ToggleGroup(viewModel.Groups.Single(g => g.Name == "Applications"));

        // Otherwise a match inside a collapsed section is invisible and the
        // search looks like it found nothing.
        viewModel.Query = "firefox";

        Assert.All(viewModel.Groups, group => Assert.True(group.IsExpanded));
    }

    [Fact]
    public void ClearingTheSearchRestoresTheCollapsedSections()
    {
        var viewModel = Loaded();
        viewModel.ToggleGroup(viewModel.Groups.Single(g => g.Name == "Applications"));

        viewModel.Query = "firefox";
        viewModel.Query = string.Empty;

        Assert.False(viewModel.Groups.Single(g => g.Name == "Applications").IsExpanded);
    }

    [Fact]
    public void SetAllExpandedOpensAndClosesEverything()
    {
        var viewModel = Loaded();

        viewModel.SetAllExpanded(false);
        Assert.All(viewModel.Groups, group => Assert.False(group.IsExpanded));

        viewModel.SetAllExpanded(true);
        Assert.All(viewModel.Groups, group => Assert.True(group.IsExpanded));

        // And the reopened state has to survive a rebuild, not just the moment.
        viewModel.Filter = FilterMode.All;
        Assert.All(viewModel.Groups, group => Assert.True(group.IsExpanded));
    }

    [Fact]
    public void UserBindingsAreListedFirst()
    {
        Assert.Equal("Your bindings", Loaded().Groups[0].Name);
    }
}
