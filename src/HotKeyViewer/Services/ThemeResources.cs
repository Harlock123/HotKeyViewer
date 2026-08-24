using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace HotKeyViewer.Services;

/// <summary>
/// Publishes the active theme's colours as the brushes the XAML binds to.
/// </summary>
/// <remarks>
/// Only the three anchors — background, foreground and accent — come straight
/// from the theme. Every other tone is mixed from them, so switching to a theme
/// that defines nothing but the basics still yields readable borders, muted text
/// and hover states, and a light theme stays light throughout.
/// </remarks>
public static class ThemeResources
{
    public static void Apply(Application application, ThemePalette palette)
    {
        var resources = application.Resources;

        void Set(string key, Color color) => resources[key] = new SolidColorBrush(color);

        // Surfaces.
        Set("SurfaceBrush", palette.Background);
        Set("SurfaceBorderBrush", palette.Mix(0.20));
        Set("RowHoverBrush", palette.Mix(0.08));

        // Text, stepping down in emphasis.
        Set("TextPrimaryBrush", palette.Foreground);
        Set("TextSecondaryBrush", palette.Mix(0.62));
        Set("TextMutedBrush", palette.Mix(0.45));

        Set("AccentBrush", palette.Accent);

        // Keycaps sit just above the surface, with a slightly stronger edge.
        Set("KeycapBrush", palette.Mix(0.11));
        Set("KeycapBorderBrush", palette.Mix(0.24));

        // Filter chips and the search field share the keycap surface; the
        // selected chip is tinted with the accent.
        Set("ChipBrush", palette.Mix(0.10));
        Set("ChipHoverBrush", palette.Mix(0.16));
        Set("ChipCheckedBrush", ThemePalette.Lerp(palette.Background, palette.Accent, 0.35));
        Set("BadgeBrush", palette.Mix(0.13));

        // Badges reuse the palette's own green and yellow so they read as part
        // of the theme rather than fixed brand colours.
        Set("CustomBrush", palette.AnsiOr(2, palette.Accent));
        Set("RemapBrush", palette.AnsiOr(3, palette.Accent));

        // Fluent's own controls (scrollbars, the text box) follow the variant,
        // so a light theme must not leave dark chrome behind.
        application.RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
