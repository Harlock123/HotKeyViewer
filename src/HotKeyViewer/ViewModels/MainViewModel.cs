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

    /// <summary>
    /// Headers and rows in one flat list. A single ListBox over this is what
    /// makes arrow keys walk the whole window; nested lists would each own a
    /// separate selection.
    /// </summary>
    public ObservableCollection<object> Rows { get; } = [];

    private object? _selectedRow;

    public object? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                RaisePropertyChanged(nameof(SelectedHotKey));
            }
        }
    }

    /// <summary>The selected row when it is a binding rather than a heading.</summary>
    public HotKey? SelectedHotKey => SelectedRow as HotKey;

    /// <summary>Where overrides are written, captured from the last load.</summary>
    public string ConfigDirectory { get; private set; } = string.Empty;

    public bool IsLuaConfig { get; private set; }

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

    private RemovalRequest? _pendingRemoval;

    /// <summary>The removal waiting on the user's confirmation, if any.</summary>
    public RemovalRequest? PendingRemoval
    {
        get => _pendingRemoval;
        set
        {
            if (SetProperty(ref _pendingRemoval, value))
            {
                RaisePropertyChanged(nameof(HasPendingRemoval));
            }
        }
    }

    public bool HasPendingRemoval => PendingRemoval is not null;

    /// <summary>
    /// Shows the result of an edit in the footer. Cleared by the next load, so a
    /// stale outcome never lingers next to refreshed data.
    /// </summary>
    public void ReportStatus(string message) => WarningText = message;

    private bool _duplicatesOnly;

    /// <summary>
    /// Narrows the list to actions reachable by more than one chord. A separate
    /// axis from the origin chips, so the two combine.
    /// </summary>
    public bool DuplicatesOnly
    {
        get => _duplicatesOnly;
        set
        {
            if (SetProperty(ref _duplicatesOnly, value))
            {
                RaisePropertyChanged(nameof(OriginFilterEnabled));
                ApplyFilter();
            }
        }
    }

    /// <summary>Hidden when nothing is duplicated, so the toggle never dead-ends.</summary>
    public bool HasDuplicates => _allHotKeys.Any(k => k.HasDuplicates);

    /// <summary>
    /// False while Duplicates is on, because the origin chips do not apply
    /// then. Greying them out is what keeps that visible -- chips that silently
    /// stopped working would read as a bug.
    /// </summary>
    public bool OriginFilterEnabled => !DuplicatesOnly;

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

        ConfigDirectory = catalog.ConfigDirectory;
        IsLuaConfig = catalog.IsLuaConfig;

        ShowsOriginFilter = catalog.HasDefaultsLayer;
        RaisePropertyChanged(nameof(HasDuplicates));

        if (!HasDuplicates)
        {
            DuplicatesOnly = false;
        }

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
            // Duplicates deliberately overrides the origin chip instead of
            // combining with it. An action's second chord is usually a default
            // when the one in front of you is yours, so narrowing to "Yours"
            // here would hide the very partner the badge is pointing at -- the
            // badge would promise a twin that the filter had just removed.
            (DuplicatesOnly || MatchesFilter(hotKey)) &&
            (!DuplicatesOnly || hotKey.HasDuplicates) &&
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

        RebuildRows();
        HasNoResults = Groups.Count == 0 && !IsLoading;
    }

    /// <summary>Flattens the groups, skipping the contents of collapsed ones.</summary>
    private void RebuildRows()
    {
        // Selection is restored by identity afterwards, so collapsing a section
        // does not silently move the cursor somewhere else.
        var selected = SelectedRow;

        Rows.Clear();

        foreach (var group in Groups)
        {
            Rows.Add(group);

            if (!group.IsExpanded)
            {
                continue;
            }

            foreach (var hotKey in group.HotKeys)
            {
                Rows.Add(hotKey);
            }
        }

        SelectedRow = selected is not null && Rows.Contains(selected) ? selected : null;
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

        RebuildRows();
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

        RebuildRows();
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
