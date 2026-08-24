using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HotKeyViewer.Models;
using HotKeyViewer.Services;
using HotKeyViewer.ViewModels;

namespace HotKeyViewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) => SearchBox.Focus();

        // Tunnelling, so the confirmation overlay sees keys before the list or
        // the search box can consume them. A bubbling handler never receives
        // Enter here, because the focused row handles it first.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Held keys auto-repeat. Without these guards a held Delete opens a modal
    /// dialog per repeat (hundreds of them), and a held Enter launches an editor
    /// window per repeat.
    /// </summary>
    private bool _removalInProgress;

    private DateTimeOffset _lastEditorLaunch = DateTimeOffset.MinValue;

    private static readonly TimeSpan LaunchCooldown = TimeSpan.FromMilliseconds(750);

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // Clear an active search first, so Escape backs out of a filter
                // before it backs out of the app.
                if (ViewModel is { Query.Length: > 0 } filtered)
                {
                    filtered.Query = string.Empty;
                    SearchBox.Focus();
                }
                else
                {
                    Close();
                }

                e.Handled = true;
                return;

            case Key.R when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = ViewModel?.LoadAsync();
                e.Handled = true;
                return;

            // Typing filters, then Down walks into the results — arrow keys in
            // the search box would otherwise just move the caret.
            case Key.Down when SearchBox.IsFocused:
                MoveIntoList();
                e.Handled = true;
                return;

            case Key.Enter when ViewModel?.SelectedRow is HotKeyGroup group:
                ViewModel.ToggleGroup(group);
                e.Handled = true;
                return;

            case Key.Enter when ViewModel?.SelectedHotKey is { HasSource: true } source:
                OpenSource(source);
                e.Handled = true;
                return;

            case Key.Delete when ViewModel?.SelectedHotKey is { } target:
                RequestRemoval(target);
                e.Handled = true;
                return;

            case Key.E when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                // Collapse everything only once nothing is left to open, so the
                // one shortcut reads as "show me less" then "show me more".
                ViewModel?.SetAllExpanded(ViewModel.Groups.Any(g => !g.IsExpanded));
                e.Handled = true;
                return;
        }

        // Anything printable typed outside the search box means the user wants
        // to search. Deliberately no bare-letter shortcuts: clicking a row moves
        // focus off the box, and a "q closes the app" binding would then fire on
        // someone simply typing a query.
        if (!SearchBox.IsFocused && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            SearchBox.Focus();
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// While a confirmation is pending the overlay owns the keyboard: Enter
    /// applies, Escape cancels, and everything else is swallowed so nothing
    /// scrolls or filters behind the prompt.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.PendingRemoval is not { } pending)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.PendingRemoval = null;
                break;

            case Key.Enter or Key.Return:
                ViewModel.PendingRemoval = null;
                _ = ApplyRemovalAsync(pending);
                break;
        }

        e.Handled = true;
    }

    /// <summary>Hands focus to the results, selecting the first row if nothing is.</summary>
    /// <remarks>
    /// Focus has to land on the row container, not the list: while the search
    /// box keeps focus the TextBox consumes Delete as "delete the next
    /// character", and the binding is never reached. The move is deferred
    /// because the container for a freshly selected row does not exist until the
    /// next layout pass.
    /// </remarks>
    private void MoveIntoList()
    {
        if (ViewModel is not { Rows.Count: > 0 } viewModel)
        {
            return;
        }

        // Land on a binding rather than a heading, so Delete and Enter are
        // immediately meaningful.
        viewModel.SelectedRow ??= viewModel.Rows.FirstOrDefault(r => r is HotKey) ?? viewModel.Rows[0];

        Dispatcher.UIThread.Post(
            () =>
            {
                var index = viewModel.SelectedRow is null ? -1 : viewModel.Rows.IndexOf(viewModel.SelectedRow);

                if (index >= 0 && RowList.ContainerFromIndex(index) is Control container)
                {
                    container.Focus(NavigationMethod.Directional);
                }
                else
                {
                    RowList.Focus(NavigationMethod.Directional);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HotKeyGroup group })
        {
            ViewModel?.ToggleGroup(group);
        }
    }

    /// <summary>
    /// Works out how the binding would be removed and puts it up for
    /// confirmation. Nothing is written until the user agrees.
    /// </summary>
    private void RequestRemoval(HotKey hotKey)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var plan = BindingRemover.Plan(hotKey, viewModel.ConfigDirectory, viewModel.IsLuaConfig);

        if (!plan.CanApply)
        {
            viewModel.ReportStatus(plan.Explanation);
            return;
        }

        var preview = plan.Kind == RemovalKind.CommentOut
            ? ReadLine(plan.TargetFile, plan.TargetLine)
            : plan.TextToAppend;

        viewModel.PendingRemoval = new RemovalRequest(hotKey, plan, preview);
    }

    /// <summary>
    /// Applies a confirmed removal, then reloads so the list reflects what the
    /// compositor actually has rather than what was asked for.
    /// </summary>
    private async Task ApplyRemovalAsync(RemovalRequest request)
    {
        if (_removalInProgress || ViewModel is not { } viewModel)
        {
            return;
        }

        _removalInProgress = true;

        try
        {
            var result = await BindingRemover.ApplyAsync(request.Plan, request.HotKey);
            viewModel.ReportStatus(result.Message);

            if (result.Succeeded)
            {
                await viewModel.LoadAsync();
            }
        }
        finally
        {
            _removalInProgress = false;
        }
    }

    private static string ReadLine(string file, int line)
    {
        try
        {
            var lines = File.ReadAllLines(file);
            return line >= 1 && line <= lines.Length ? lines[line - 1].Trim() : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private void OnConfirmRemoval(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.PendingRemoval is { } request)
        {
            ViewModel.PendingRemoval = null;
            _ = ApplyRemovalAsync(request);
        }
    }

    private void OnCancelRemoval(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PendingRemoval = null;
        }
    }

    private void OnOpenSource(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HotKey hotKey })
        {
            OpenSource(hotKey);
        }
    }

    /// <summary>Opens a binding's source, ignoring repeats from a held key.</summary>
    private void OpenSource(HotKey hotKey)
    {
        if (!hotKey.HasSource || DateTimeOffset.UtcNow - _lastEditorLaunch < LaunchCooldown)
        {
            return;
        }

        _lastEditorLaunch = DateTimeOffset.UtcNow;
        EditorLauncher.Open(hotKey.SourceFile, hotKey.SourceLine);
    }

    private void OnFilterAll(object? sender, RoutedEventArgs e) => SetFilter(FilterMode.All);

    private void OnFilterCustomised(object? sender, RoutedEventArgs e) => SetFilter(FilterMode.Customised);

    private void OnFilterDefaults(object? sender, RoutedEventArgs e) => SetFilter(FilterMode.Defaults);

    private void SetFilter(FilterMode mode)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.Filter = mode;
        }

        // Filtering refines the current search, so hand typing straight back.
        SearchBox.Focus();
    }
}
