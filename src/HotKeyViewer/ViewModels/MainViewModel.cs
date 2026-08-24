using System.Collections.ObjectModel;
using HotKeyViewer.Models;
using HotKeyViewer.Services;

namespace HotKeyViewer.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private static readonly string[] CategoryOrder =
    [
        "Your bindings",
        "Applications",
        "Windows & Workspaces",
        "Clipboard & Text",
        "Utilities",
        "Toggles",
        "Media & Hardware",
        "Dictation",
    ];

    /// <summary>
    /// Categories the user has collapsed, remembered by name because the groups
    /// themselves are rebuilt on every keystroke.
    /// </summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    private IReadOnlyList<HotKey> _allHotKeys = [];
    private string _query = string.Empty;
    private FilterMode _filter = FilterMode.All;
    private bool _isLoading = true;
    private string _statusText = "Reading Hyprland configuration…";
    private string _warningText = string.Empty;

    public ObservableCollection<HotKeyGroup> Groups { get; } = [];

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                ApplyFilter();
            }
        }
    }

    public FilterMode Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                RaisePropertyChanged(nameof(IsShowingAll));
                RaisePropertyChanged(nameof(IsShowingCustomised));
                RaisePropertyChanged(nameof(IsShowingDefaults));
                ApplyFilter();
            }
        }
    }

    // Bound to the filter buttons' checked state.
    public bool IsShowingAll => Filter == FilterMode.All;
    public bool IsShowingCustomised => Filter == FilterMode.Customised;
    public bool IsShowingDefaults => Filter == FilterMode.Defaults;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaisePropertyChanged(nameof(IsReady));
            }
        }
    }

    public bool IsReady => !IsLoading;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string WarningText
    {
        get => _warningText;
        private set
        {
            if (SetProperty(ref _warningText, value))
            {
                RaisePropertyChanged(nameof(HasWarning));
            }
        }
    }

    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    private bool _showsOriginFilter = true;

    /// <summary>
    /// Whether the Yours/Defaults chips are worth showing. On a stock Hyprland
    /// install every binding is the user's own, so the filter would offer a
    /// choice between everything and nothing.
    /// </summary>
    public bool ShowsOriginFilter
    {
        get => _showsOriginFilter;
        private set => SetProperty(ref _showsOriginFilter, value);
    }

    private bool _hasNoResults;

    public bool HasNoResults
    {
        get => _hasNoResults;
        private set => SetProperty(ref _hasNoResults, value);
    }

    public int TotalCount => _allHotKeys.Count;

    public int CustomCount => _allHotKeys.Count(k => k.IsCustom);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;

        var catalog = await HotKeyCatalogBuilder.BuildAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        Load(catalog);
    }

    /// <summary>
    /// Populates the view from an already-built catalog. Split out from
    /// <see cref="LoadAsync"/> so the filtering and grouping can be exercised
    /// without a compositor or a config directory to read from.
    /// </summary>
    public void Load(HotKeyCatalog catalog)
    {
        _allHotKeys = catalog.HotKeys;

        ShowsOriginFilter = catalog.HasDefaultsLayer;
        if (!ShowsOriginFilter)
        {
            Filter = FilterMode.All;
        }

        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(CustomCount));

        var files = catalog.FilesScanned.Count;
        var scanned = $"{files} config file{(files == 1 ? "" : "s")} scanned";

        StatusText = catalog.HasDefaultsLayer
            ? $"{catalog.HotKeys.Count} bindings · {CustomCount} yours · {scanned}"
            : $"{catalog.HotKeys.Count} bindings · {scanned}";
        WarningText = string.Join("  •  ", catalog.Warnings.Take(3));

        IsLoading = false;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var terms = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        var matches = _allHotKeys.Where(hotKey =>
            MatchesFilter(hotKey) &&
            // Every term must appear somewhere, so "super work" narrows rather
            // than widens.
            terms.All(term => hotKey.SearchText.Contains(term, StringComparison.Ordinal)));

        // While a search is running every section is forced open: leaving a
        // collapsed section shut would hide matches and read as "no results".
        var searching = terms.Length > 0;

        var grouped = matches
            .GroupBy(k => k.Category)
            .Select(group => new HotKeyGroup(
                group.Key,
                [.. group.OrderBy(k => k.Description, StringComparer.OrdinalIgnoreCase)])
            {
                IsExpanded = searching || !_collapsed.Contains(group.Key),
            })
            .OrderBy(group => RankCategory(group.Name))
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase);

        Groups.Clear();
        foreach (var group in grouped)
        {
            Groups.Add(group);
        }

        HasNoResults = Groups.Count == 0 && !IsLoading;
    }

    /// <summary>Opens or closes one section, remembering the choice.</summary>
    public void ToggleGroup(HotKeyGroup group)
    {
        group.IsExpanded = !group.IsExpanded;

        if (group.IsExpanded)
        {
            _collapsed.Remove(group.Name);
        }
        else
        {
            _collapsed.Add(group.Name);
        }
    }

    /// <summary>Opens or closes every section at once.</summary>
    public void SetAllExpanded(bool expanded)
    {
        _collapsed.Clear();

        if (!expanded)
        {
            foreach (var group in Groups)
            {
                _collapsed.Add(group.Name);
            }
        }

        foreach (var group in Groups)
        {
            group.IsExpanded = expanded;
        }
    }

    private bool MatchesFilter(HotKey hotKey) => Filter switch
    {
        FilterMode.Customised => hotKey.IsCustom,
        FilterMode.Defaults => !hotKey.IsCustom,
        _ => true,
    };

    private static int RankCategory(string name)
    {
        var index = Array.IndexOf(CategoryOrder, name);
        return index >= 0 ? index : CategoryOrder.Length;
    }
}
