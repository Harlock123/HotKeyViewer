using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HotKeyViewer.Models;
using HotKeyViewer.Services;
using HotKeyViewer.ViewModels;
using HotKeyViewer.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(HotKeyViewer.Tests.TestAppBuilder))]

namespace HotKeyViewer.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .WithInterFont();
}

/// <summary>
/// Drives the real window with real key events. Key handling here is a question
/// of routing — which control sees a key first — and routing is invisible to a
/// test that calls the handler directly, which is exactly how the Enter-opens-
/// the-editor bug shipped with tests passing.
/// </summary>
public class KeyboardNavigationTests
{
    /// <summary>
    /// Avalonia has one UI thread per test assembly, so every case runs through
    /// the shared session rather than constructing windows on the xunit thread.
    /// </summary>
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(KeyboardNavigationTests).Assembly);

    private static Task OnUiThread(Action body) => Session.Dispatch(body, CancellationToken.None);

    private static HotKey Make(string chord, string description) =>
        new()
        {
            Chord = KeyChord.Parse(chord),
            RawChord = KeyChord.Parse(chord),
            Description = description,
            Command = "run-" + description,
            Origin = BindOrigin.Default,
            Category = "Applications",
            SearchText = $"{chord} {description}".ToLowerInvariant(),
        };

    /// <summary>A full press and release, as a real keyboard delivers it.</summary>
    private static void Press(MainWindow window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Opens the window in the state a user gets it in: search box focused, nothing selected.</summary>
    private static (MainWindow Window, MainViewModel ViewModel) Open()
    {
        var viewModel = new MainViewModel();

        viewModel.Load(new HotKeyCatalog(
            [Make("SUPER + A", "Alpha"), Make("SUPER + B", "Bravo"), Make("SUPER + C", "Charlie")],
            ["hyprland.lua"],
            []));

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.SearchBox.IsFocused, "the search box should hold focus when the window opens");
        Assert.Null(viewModel.SelectedRow);

        return (window, viewModel);
    }

    [Fact]
    public Task DownFromTheSearchBoxSelectsABinding() => OnUiThread(() =>
    {
        var (window, viewModel) = Open();

        Press(window, Key.Down, PhysicalKey.ArrowDown);

        Assert.IsType<HotKey>(viewModel.SelectedRow);
    });

    [Fact]
    public Task UpFromTheSearchBoxAlsoEntersTheList() => OnUiThread(() =>
    {
        var (window, viewModel) = Open();

        Press(window, Key.Up, PhysicalKey.ArrowUp);

        Assert.IsType<HotKey>(viewModel.SelectedRow);
    });

    [Fact]
    public Task TheFirstArrowLandsOnABindingRatherThanAHeading() => OnUiThread(() =>
    {
        var (window, viewModel) = Open();

        Press(window, Key.Down, PhysicalKey.ArrowDown);

        Assert.Equal("Alpha", Assert.IsType<HotKey>(viewModel.SelectedRow).Description);
    });

    [Fact]
    public Task ArrowsMoveFocusOutOfTheSearchBox() => OnUiThread(() =>
    {
        var (window, _) = Open();

        Press(window, Key.Down, PhysicalKey.ArrowDown);

        // Focus has to leave the box, or the TextBox keeps eating Delete as
        // "delete the next character" and removal never reaches the binding.
        Assert.False(window.SearchBox.IsFocused);
    });

    /// <summary>
    /// The case that actually bites on Wayland: a client cannot self-activate,
    /// so the focus set in the Opened handler does not always stick and the
    /// window arrives with focus on nothing. Keying off "focus is not in the
    /// list" rather than "the search box has focus" covers it either way.
    /// </summary>
    [Fact]
    public Task ArrowsWorkEvenIfTheSearchBoxNeverTookFocus() => OnUiThread(() =>
    {
        var (window, viewModel) = Open();

        window.FocusManager?.Focus(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.SearchBox.IsFocused);

        Press(window, Key.Down, PhysicalKey.ArrowDown);

        Assert.IsType<HotKey>(viewModel.SelectedRow);
    });

    [Fact]
    public Task TypingStillReachesTheSearchBox() => OnUiThread(() =>
    {
        var (window, viewModel) = Open();

        window.KeyTextInput("brav");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("brav", viewModel.Query);
    });
}
