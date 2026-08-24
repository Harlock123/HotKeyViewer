using System.Globalization;
using Avalonia.Media;

namespace HotKeyViewer.Services;

/// <summary>
/// The colours of the active Omarchy theme, as published in its
/// <c>colors.toml</c>.
/// </summary>
/// <remarks>
/// The palette is terminal-shaped — a foreground, a background, an accent and
/// sixteen ANSI slots — because every other themed app on the system consumes
/// it that way. UI roles are derived from it rather than stored, so a theme that
/// only sets the basics still produces a complete, legible window.
/// </remarks>
public sealed record ThemePalette
{
    public required Color Background { get; init; }
    public required Color Foreground { get; init; }
    public required Color Accent { get; init; }

    /// <summary>
    /// The ANSI slots the theme defined, keyed by index. Sparse on purpose: a
    /// theme that sets only some slots must not lose the ones after the gap.
    /// </summary>
    public required IReadOnlyDictionary<int, Color> Ansi { get; init; }

    /// <summary>The built-in palette, used when Omarchy is not installed.</summary>
    public static readonly ThemePalette Fallback = new()
    {
        Background = Color.FromRgb(0x17, 0x17, 0x1D),
        Foreground = Color.FromRgb(0xF2, 0xF2, 0xF5),
        Accent = Color.FromRgb(0x7A, 0xA2, 0xF7),
        Ansi = new Dictionary<int, Color>(),
    };

    /// <summary>
    /// True when the background is light, so the rest of the UI can switch to a
    /// light control theme instead of leaving dark scrollbars on a pale window.
    /// </summary>
    public bool IsLight => Luminance(Background) > 0.5;

    /// <summary>An ANSI colour if the theme defined it, else a sensible stand-in.</summary>
    public Color AnsiOr(int index, Color fallback) =>
        Ansi.TryGetValue(index, out var color) ? color : fallback;

    /// <summary>
    /// Blends toward the foreground by <paramref name="amount"/>. Used for every
    /// intermediate tone so the result stays legible whether the theme is light
    /// or dark — the mix always moves away from the background.
    /// </summary>
    public Color Mix(double amount) => Lerp(Background, Foreground, amount);

    public static Color Lerp(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        return Color.FromRgb(
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    /// <summary>Perceptual luminance, for deciding light versus dark.</summary>
    private static double Luminance(Color color) =>
        ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
}

/// <summary>Reads the active Omarchy theme and reports when it changes.</summary>
public static class OmarchyTheme
{
    /// <summary>
    /// The directory Omarchy keeps the live theme in. It replaces the whole
    /// <c>theme</c> directory on every switch, so nothing inside it can be
    /// watched directly.
    /// </summary>
    public static string CurrentDirectory { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
        "omarchy",
        "current");

    private static string ColorsFile => Path.Combine(CurrentDirectory, "theme", "colors.toml");

    /// <summary>
    /// Written last by omarchy-theme-set, after the new theme directory is in
    /// place, which makes it the one safe signal that a switch has completed.
    /// </summary>
    private const string ChangeSignalFile = "theme.name";

    public static ThemePalette Read()
    {
        try
        {
            return File.Exists(ColorsFile)
                ? Parse(File.ReadAllText(ColorsFile))
                : ThemePalette.Fallback;
        }
        catch (IOException)
        {
            // Mid-switch the file can vanish for an instant; the watcher will
            // fire again once it settles.
            return ThemePalette.Fallback;
        }
    }

    internal static ThemePalette Parse(string toml)
    {
        var values = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in toml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim().Trim('"', '\'');

            if (ParseHex(value) is { } color)
            {
                values[key] = color;
            }
        }

        var ansi = new Dictionary<int, Color>();
        for (var index = 0; index < 16; index++)
        {
            if (values.TryGetValue($"color{index}", out var color))
            {
                ansi[index] = color;
            }
        }

        var background = values.GetValueOrDefault("background", ThemePalette.Fallback.Background);
        var foreground = values.GetValueOrDefault("foreground", ThemePalette.Fallback.Foreground);

        return new ThemePalette
        {
            Background = background,
            Foreground = foreground,
            // Not every theme sets an accent; the blue ANSI slot is the usual
            // stand-in and is always more on-theme than a hardcoded colour.
            Accent = values.GetValueOrDefault(
                "accent",
                ansi.GetValueOrDefault(4, ThemePalette.Fallback.Accent)),
            Ansi = ansi,
        };
    }

    internal static Color? ParseHex(string value)
    {
        var text = value.TrimStart('#');

        if (text.Length is not (6 or 8) ||
            !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return null;
        }

        // Themes write #rrggbb; the eight-digit form is treated as #rrggbbaa,
        // matching how the terminal configs in the same directory spell it.
        return text.Length == 6
            ? Color.FromRgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed)
            : Color.FromArgb((byte)packed, (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8));
    }

    /// <summary>
    /// Calls <paramref name="onChanged"/> whenever the theme is switched.
    /// Returns the watcher, which the caller keeps alive for the app's lifetime.
    /// </summary>
    public static FileSystemWatcher? Watch(Action onChanged)
    {
        if (!Directory.Exists(CurrentDirectory))
        {
            return null;
        }

        try
        {
            // Watching the parent directory, not the theme directory: the latter
            // is deleted and recreated on every switch, which would silently
            // invalidate a watch placed on it.
            var watcher = new FileSystemWatcher(CurrentDirectory, ChangeSignalFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };

            watcher.Changed += (_, _) => onChanged();
            watcher.Created += (_, _) => onChanged();
            watcher.Renamed += (_, _) => onChanged();

            return watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without inotify the app simply keeps the theme it started with.
            return null;
        }
    }
}
