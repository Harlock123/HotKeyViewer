using System.Text.RegularExpressions;
using HotKeyViewer.Services;

namespace HotKeyViewer.Sources;

/// <summary>
/// Translates the <c>code:NN</c> form that Hyprland reports for keycode-based
/// binds into the symbol actually printed on the key.
/// </summary>
/// <remarks>
/// Binding by keycode is how a config stays layout-independent — Omarchy binds
/// the workspace keys as <c>code:10</c>…<c>code:19</c> so they work on AZERTY
/// too — but it means the config never names the key. The compiled XKB keymap
/// is the only thing that knows what <c>code:10</c> prints on this keyboard.
/// </remarks>
public sealed partial class KeycodeResolver
{
    // Enough of the US layout to keep the common binds readable when xkbcli is
    // unavailable; anything else falls back to showing "code:NN" verbatim.
    private static readonly Dictionary<int, string> Fallback = new()
    {
        [10] = "1", [11] = "2", [12] = "3", [13] = "4", [14] = "5",
        [15] = "6", [16] = "7", [17] = "8", [18] = "9", [19] = "0",
        [20] = "MINUS", [21] = "EQUAL", [59] = "COMMA", [60] = "PERIOD", [61] = "SLASH",
    };

    // X keysym names are what the keymap yields; these are what the key prints.
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MINUS"] = "-", ["EQUAL"] = "=", ["COMMA"] = ",", ["PERIOD"] = ".",
        ["SLASH"] = "/", ["BACKSLASH"] = "\\", ["SEMICOLON"] = ";", ["APOSTROPHE"] = "'",
        ["GRAVE"] = "`", ["BRACKETLEFT"] = "[", ["BRACKETRIGHT"] = "]",
    };

    private static readonly Dictionary<string, string> MouseButtons = new()
    {
        ["272"] = "LEFT CLICK",
        ["273"] = "RIGHT CLICK",
        ["274"] = "MIDDLE CLICK",
        ["275"] = "MOUSE BACK",
        ["276"] = "MOUSE FORWARD",
    };

    private readonly Dictionary<int, string> _symbols;

    private KeycodeResolver(Dictionary<int, string> symbols) => _symbols = symbols;

    /// <summary>
    /// The built-in table only, without consulting the compiled keymap. Used by
    /// tests, and on systems with no xkbcli.
    /// </summary>
    internal static KeycodeResolver BuiltIn => new(new Dictionary<int, string>(Fallback));

    [GeneratedRegex(@"<([A-Za-z0-9_]+)>\s*=\s*(\d+)\s*;")]
    private static partial Regex KeycodeLine { get; }

    [GeneratedRegex(@"key\s*<([A-Za-z0-9_]+)>\s*\{\s*\[\s*([^,\s\]]+)")]
    private static partial Regex SymbolLine { get; }

    public static async Task<KeycodeResolver> LoadAsync(CancellationToken cancellationToken = default)
    {
        var symbols = new Dictionary<int, string>(Fallback);

        // Routed through a shell purely to hand xkbcli /dev/null on stdin.
        // Given an empty pipe instead, it decides stdin is a keymap to parse and
        // fails with "Couldn't read XKB file"; /dev/null, a closed fd and a tty
        // all behave. .NET cannot attach /dev/null to a child fd directly, and
        // simply inheriting our own stdin would reintroduce the pipe case
        // whenever this app is itself launched from one.
        var result = await ProcessRunner
            .RunAsync("sh", ["-c", "exec xkbcli compile-keymap </dev/null"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return new KeycodeResolver(symbols);
        }

        // The keymap lists names against numbers in one section and names
        // against symbols in another, so both have to be read before joining.
        var codeByName = new Dictionary<string, int>();
        var symbolByName = new Dictionary<string, string>();
        var section = string.Empty;

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            if (line.Contains("xkb_keycodes", StringComparison.Ordinal))
            {
                section = "keycodes";
                continue;
            }

            if (line.Contains("xkb_symbols", StringComparison.Ordinal))
            {
                section = "symbols";
                continue;
            }

            if (section == "keycodes" && KeycodeLine.Match(line) is { Success: true } code)
            {
                codeByName[code.Groups[1].Value] = int.Parse(code.Groups[2].Value);
            }
            else if (section == "symbols" && SymbolLine.Match(line) is { Success: true } symbol)
            {
                symbolByName[symbol.Groups[1].Value] = symbol.Groups[2].Value;
            }
        }

        foreach (var (name, code) in codeByName)
        {
            if (symbolByName.TryGetValue(name, out var symbol) &&
                !string.IsNullOrEmpty(symbol) &&
                !symbol.Equals("NoSymbol", StringComparison.OrdinalIgnoreCase))
            {
                symbols[code] = symbol.ToUpperInvariant();
            }
        }

        return new KeycodeResolver(symbols);
    }

    /// <summary>
    /// Turns a raw key token into something printable. Passes through anything
    /// that is already a plain key name.
    /// </summary>
    public string Resolve(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        if (key.StartsWith("code:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key.AsSpan(5), out var code))
        {
            // A keycode with no symbol in this keymap (a vendor key, say) has
            // nothing better to show than the code itself.
            return _symbols.TryGetValue(code, out var symbol) ? Prettify(symbol) : key;
        }

        if (key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase))
        {
            return MouseButtons.TryGetValue(key[6..], out var button) ? button : key;
        }

        // Lid and tablet-mode switches are bound like keys and reported the
        // same way; "switch:on:Lid Switch" is unreadable on a keycap.
        if (key.StartsWith("switch:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = key.Split(':', 3);
            if (parts.Length == 3)
            {
                return $"{parts[2].ToUpperInvariant()} {(parts[1] == "on" ? "ON" : "OFF")}";
            }
        }

        return key switch
        {
            "mouse_up" => "SCROLL UP",
            "mouse_down" => "SCROLL DOWN",
            _ => Prettify(key),
        };
    }

    private static string Prettify(string symbol) =>
        Glyphs.TryGetValue(symbol, out var glyph) ? glyph : symbol;
}
