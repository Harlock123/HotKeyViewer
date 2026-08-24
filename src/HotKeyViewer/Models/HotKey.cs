namespace HotKeyViewer.Models;

/// <summary>Where a binding was defined, which is what separates a user's own
/// bindings from the ones their distribution shipped.</summary>
public enum BindOrigin
{
    /// <summary>Active, but no config file claimed it (e.g. a plugin).</summary>
    Unknown,

    /// <summary>Defined under a system path such as /usr/share/omarchy.</summary>
    Default,

    /// <summary>Defined in the user's own config, under ~/.config/hypr.</summary>
    User,
}

/// <summary>Bind modifiers reported by Hyprland in the <c>bind…</c> header.</summary>
[Flags]
public enum BindOptions
{
    None = 0,
    Locked = 1 << 0,
    Release = 1 << 1,
    Repeats = 1 << 2,
    LongPress = 1 << 3,
    Mouse = 1 << 4,
    NonConsuming = 1 << 5,
    IgnoreMods = 1 << 6,
    Transparent = 1 << 7,
    HasDescription = 1 << 8,
}

/// <summary>A single hotkey as it is currently live in Hyprland, enriched with
/// whatever the config files could tell us about it.</summary>
public sealed record HotKey
{
    public required KeyChord Chord { get; init; }

    /// <summary>Human label from the bind's description, falling back to the command.</summary>
    public required string Description { get; init; }

    /// <summary>What the binding runs: a shell command or a dispatcher call.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Raw dispatcher name as Hyprland reports it (often "__lua").</summary>
    public string Dispatcher { get; init; } = string.Empty;

    public string Submap { get; init; } = string.Empty;

    public BindOptions Options { get; init; } = BindOptions.None;

    public BindOrigin Origin { get; init; } = BindOrigin.Unknown;

    /// <summary>Absolute path of the file that defined this bind, when known.</summary>
    public string SourceFile { get; init; } = string.Empty;

    public int SourceLine { get; init; }

    /// <summary>True when the user's config replaced a binding the defaults also set.</summary>
    public bool IsOverride { get; init; }

    /// <summary>Grouping label, derived from the defining file or the description.</summary>
    public string Category { get; init; } = "Other";

    public bool IsCustom => Origin == BindOrigin.User || IsOverride;

    /// <summary>Short "bindings.lua:83" style label for the source.</summary>
    public string SourceLabel => string.IsNullOrEmpty(SourceFile)
        ? string.Empty
        : $"{Path.GetFileName(SourceFile)}:{SourceLine}";

    public bool HasSource => SourceLabel.Length > 0;

    /// <summary>
    /// False when the command would only repeat the label, which happens for
    /// binds that carry no description of their own.
    /// </summary>
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command)
        && !Command.Equals(Description, StringComparison.Ordinal);

    /// <summary>
    /// The chord as the config spells it, before keycodes are resolved to the
    /// symbols they print. An unbind has to name "SUPER + code:10", not the
    /// "SUPER + 1" shown on screen, or it will not match.
    /// </summary>
    public required KeyChord RawChord { get; init; }

    /// <summary>
    /// How many bindings the defining line produces. Greater than one means it
    /// is a loop — Omarchy's `for workspace = 1, 10` makes ten bindings from one
    /// line — so that line can never be deleted to remove just this binding.
    /// </summary>
    public int DefinitionShareCount { get; init; } = 1;

    /// <summary>
    /// How many chords run this same command, counting this one. Greater than
    /// one means the same action is reachable more than one way.
    /// </summary>
    public int DuplicateCount { get; init; } = 1;

    public bool HasDuplicates => DuplicateCount > 1;

    public string DuplicateLabel => $"{DuplicateCount} KEYS";

    /// <summary>
    /// True when the defining file belongs to the distribution, where edits are
    /// overwritten by the next update.
    /// </summary>
    public bool IsReadOnlySource => Origin == BindOrigin.Default;

    /// <summary>Hover text for the source link, since the label is only a name.</summary>
    public string SourceTooltip => IsReadOnlySource
        ? $"{SourceFile}:{SourceLine}\nProvided by your distribution — edits here are overwritten on update."
        : $"{SourceFile}:{SourceLine}";

    /// <summary>Tag shown next to bindings the user is responsible for.</summary>
    public string BadgeText => IsOverride
        ? "REMAPPED"
        : Origin == BindOrigin.User ? "YOURS" : string.Empty;

    public bool HasBadge => BadgeText.Length > 0;

    /// <summary>Everything the search box matches against, lowercased once.</summary>
    public string SearchText { get; init; } = string.Empty;
}
