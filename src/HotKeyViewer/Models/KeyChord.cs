namespace HotKeyViewer.Models;

/// <summary>
/// A modifier-plus-key combination, normalised so that records coming from
/// <c>hyprctl</c> (which reports a numeric mask) and from config files (which
/// spell the modifiers out) can be compared to each other.
/// </summary>
public readonly record struct KeyChord(int ModMask, string Key)
{
    // Hyprland's mask, which follows the XKB modifier order.
    private static readonly (int Bit, string Name)[] ModifierBits =
    [
        (64, "SUPER"),
        (4, "CTRL"),
        (8, "ALT"),
        (1, "SHIFT"),
        (2, "CAPS"),
        (16, "MOD2"),
        (32, "MOD3"),
        (128, "MOD5"),
    ];

    private static readonly Dictionary<string, int> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SHIFT"] = 1,
        ["CAPS"] = 2,
        ["CAPSLOCK"] = 2,
        ["CTRL"] = 4,
        ["CONTROL"] = 4,
        ["ALT"] = 8,
        ["MOD1"] = 8,
        ["MOD2"] = 16,
        ["MOD3"] = 32,
        ["SUPER"] = 64,
        ["SUPER_L"] = 64,
        ["SUPER_R"] = 64,
        ["MOD4"] = 64,
        ["WIN"] = 64,
        ["LOGO"] = 64,
        ["MOD5"] = 128,
    };

    /// <summary>Modifier names in a stable display order, e.g. SUPER, SHIFT.</summary>
    public IReadOnlyList<string> Modifiers
    {
        get
        {
            // Copied to a local because a lambda in a struct cannot touch `this`.
            var mask = ModMask;
            return ModifierBits.Where(m => (mask & m.Bit) != 0).Select(m => m.Name).ToArray();
        }
    }

    /// <summary>The parts to render as individual keycaps, modifiers first.</summary>
    public IReadOnlyList<string> Parts =>
        string.IsNullOrEmpty(Key) ? Modifiers : [.. Modifiers, Key];

    public string Display => string.Join(" + ", Parts);

    /// <summary>
    /// Case-insensitive identity used to match the same binding across sources.
    /// </summary>
    public string MatchKey => $"{ModMask}|{Key.ToUpperInvariant()}";

    /// <summary>
    /// Parses a chord written the way config files spell it, such as
    /// "SUPER + SHIFT + RETURN" or "SUPER + code:10".
    /// </summary>
    public static KeyChord Parse(string text)
    {
        var mask = 0;
        var key = string.Empty;

        foreach (var raw in (text ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (ModifierNames.TryGetValue(part, out var bit))
            {
                // Or rather than add: a chord may name the same modifier twice.
                mask |= bit;
            }
            else
            {
                key = part;
            }
        }

        return new KeyChord(mask, key);
    }
}
