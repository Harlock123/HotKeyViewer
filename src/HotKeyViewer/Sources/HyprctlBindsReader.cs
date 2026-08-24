using HotKeyViewer.Models;
using HotKeyViewer.Services;

namespace HotKeyViewer.Sources;

/// <summary>One bind exactly as Hyprland currently holds it.</summary>
public sealed record LiveBind
{
    public required KeyChord Chord { get; init; }
    public string RawKey { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Dispatcher { get; init; } = string.Empty;
    public string Arg { get; init; } = string.Empty;
    public string Submap { get; init; } = string.Empty;
    public BindOptions Options { get; init; }
}

/// <summary>
/// Reads the authoritative list of active bindings from the running compositor.
/// </summary>
/// <remarks>
/// Deliberately parses the plain-text <c>hyprctl binds</c> rather than the
/// <c>-j</c> JSON. Two reasons, both verified against Hyprland 0.56: the JSON
/// form drops the key for keycode binds (reporting an empty <c>key</c> and a
/// zero <c>keycode</c>, losing "SUPER + code:10" entirely), and it has a
/// history of emitting malformed JSON when a bind argument contains quotes.
/// The text form carries the full display key and degrades line-by-line.
/// </remarks>
public static class HyprctlBindsReader
{
    // Suffix letters on the "bind…" header line, e.g. "bindled" = locked,
    // repeating, described.
    private static readonly Dictionary<char, BindOptions> OptionLetters = new()
    {
        ['l'] = BindOptions.Locked,
        ['r'] = BindOptions.Release,
        ['e'] = BindOptions.Repeats,
        ['n'] = BindOptions.NonConsuming,
        ['m'] = BindOptions.Mouse,
        ['t'] = BindOptions.LongPress,
        ['i'] = BindOptions.IgnoreMods,
        ['p'] = BindOptions.Transparent,
        ['d'] = BindOptions.HasDescription,
    };

    public static bool IsHyprlandRunning =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

    public static async Task<IReadOnlyList<LiveBind>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync("hyprctl", ["binds"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? Parse(result.StandardOutput) : [];
    }

    public static IReadOnlyList<LiveBind> Parse(string text)
    {
        var binds = new List<LiveBind>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = BindOptions.None;
        var inRecord = false;

        void Flush()
        {
            if (!inRecord)
            {
                return;
            }

            inRecord = false;

            // The text form spells the key as "SUPER + code:10"; the modifiers
            // are already carried numerically in modmask, so keep only the tail.
            var key = fields.GetValueOrDefault("key", string.Empty).Trim();
            var separator = key.LastIndexOf(" + ", StringComparison.Ordinal);
            if (separator >= 0)
            {
                key = key[(separator + 3)..];
            }

            // Older Hyprland builds report the number instead of the key text.
            if (key.Length == 0 &&
                fields.TryGetValue("keycode", out var keycode) &&
                keycode.Trim() is { Length: > 0 } code && code != "0")
            {
                key = $"code:{code}";
            }

            _ = int.TryParse(fields.GetValueOrDefault("modmask", "0"), out var modmask);

            binds.Add(new LiveBind
            {
                Chord = new KeyChord(modmask, key),
                RawKey = key,
                Description = fields.GetValueOrDefault("description", string.Empty).Trim(),
                Dispatcher = fields.GetValueOrDefault("dispatcher", string.Empty).Trim(),
                Arg = fields.GetValueOrDefault("arg", string.Empty).Trim(),
                Submap = fields.GetValueOrDefault("submap", string.Empty).Trim(),
                Options = options,
            });
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("bind", StringComparison.Ordinal) && !line.StartsWith("\t", StringComparison.Ordinal))
            {
                Flush();

                fields.Clear();
                options = BindOptions.None;
                inRecord = true;

                foreach (var letter in line.AsSpan(4))
                {
                    if (OptionLetters.TryGetValue(letter, out var option))
                    {
                        options |= option;
                    }
                }

                continue;
            }

            if (!inRecord || !line.StartsWith('\t'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon > 1)
            {
                fields[line[1..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        Flush();
        return binds;
    }
}
