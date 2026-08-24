using HotKeyViewer.Models;

namespace HotKeyViewer.ViewModels;

/// <summary>Hotkeys sharing a category, as one collapsible section in the list.</summary>
public sealed class HotKeyGroup(string name, IReadOnlyList<HotKey> hotKeys) : ViewModelBase
{
    private bool _isExpanded = true;

    public string Name { get; } = name;

    public IReadOnlyList<HotKey> HotKeys { get; } = hotKeys;

    public string CountLabel => HotKeys.Count == 1 ? "1 binding" : $"{HotKeys.Count} bindings";

    /// <summary>The user's own bindings sort to the top; they are why this app exists.</summary>
    public bool IsUserGroup { get; } = name == "Your bindings";

    /// <summary>Sections start open: the list is meant to be readable at a glance.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                RaisePropertyChanged(nameof(IsCollapsed));
            }
        }
    }

    /// <summary>Lets the two chevron glyphs bind without needing a converter.</summary>
    public bool IsCollapsed => !IsExpanded;
}
