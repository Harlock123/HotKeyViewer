using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HotKeyViewer.ViewModels;

namespace HotKeyViewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) => SearchBox.Focus();
    }

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

    private void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HotKeyGroup group })
        {
            ViewModel?.ToggleGroup(group);
        }
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
