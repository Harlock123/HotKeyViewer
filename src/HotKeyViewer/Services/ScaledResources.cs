using Avalonia;
using Avalonia.Controls;

namespace HotKeyViewer.Services;

/// <summary>
/// Publishes the font and layout sizes the UI draws with, scaled by the
/// desktop's text-size preference.
/// </summary>
/// <remarks>
/// The display scale factor is not applied here — the compositor already tells
/// the toolkit about that, and doubling it up would render everything twice as
/// large as intended. This is only the separate "make text bigger" preference,
/// which GTK applies on top of the display scale and which nothing in Avalonia
/// reads on its own.
/// </remarks>
public static class ScaledResources
{
    /// <summary>Design sizes at a text scale of 1, keyed as they are in XAML.</summary>
    private static readonly (string Key, double Size)[] FontSizes =
    [
        ("FontSizeTitle", 19),
        ("FontSizeBody", 13),
        ("FontSizeSearch", 13),
        ("FontSizeHeader", 12),
        ("FontSizeChip", 12),
        ("FontSizeSmall", 11),
        ("FontSizeKeycap", 11),
        ("FontSizeBadge", 9),
    ];

    /// <summary>
    /// Fixed-width columns have to grow with the text they hold, or larger type
    /// simply gets clipped.
    /// </summary>
    private static readonly (string Key, double Width)[] ColumnWidths =
    [
        ("KeycapColumnWidth", 280),
        ("SourceColumnWidth", 140),
        ("ChevronSize", 9),
    ];

    public static void Apply(Application application, double textScale)
    {
        foreach (var (key, size) in FontSizes)
        {
            application.Resources[key] = Math.Round(size * textScale, 1);
        }

        foreach (var (key, width) in ColumnWidths)
        {
            application.Resources[key] = Math.Round(width * textScale);
        }

        // Kept as a GridLength too, for any layout that needs a column rather
        // than a width.
        application.Resources["KeycapColumn"] =
            new GridLength(Math.Round(280 * textScale), GridUnitType.Pixel);
    }
}
