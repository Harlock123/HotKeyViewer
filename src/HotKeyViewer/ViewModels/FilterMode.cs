namespace HotKeyViewer.ViewModels;

public enum FilterMode
{
    All,

    /// <summary>Only bindings the user added or remapped themselves.</summary>
    Customised,

    /// <summary>Only bindings that came from the distribution or system config.</summary>
    Defaults,
}
