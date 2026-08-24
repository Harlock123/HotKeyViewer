using System.Globalization;
using System.Text.Json;

namespace HotKeyViewer.Services;

/// <summary>
/// The display and font-size settings the desktop expects apps to honour.
/// </summary>
/// <param name="TextScale">
/// The user's text-size preference, applied on top of the display scale exactly
/// as GTK does. Independent of the monitor scale factor.
/// </param>
/// <param name="LogicalScreen">
/// The focused monitor's size in logical pixels, or null when it is unknown.
/// </param>
public sealed record DisplayMetrics(double TextScale, (double Width, double Height)? LogicalScreen)
{
    /// <summary>The design size of the window at a text scale of 1.</summary>
    private const double BaseWidth = 1080;
    private const double BaseHeight = 720;

    /// <summary>Leaves a margin so a floating window never covers the whole screen.</summary>
    private const double MaxScreenFraction = 0.9;

    public static readonly DisplayMetrics Default = new(1.0, null);

    /// <summary>
    /// The window size to ask for: the design size grown by the text scale, so
    /// larger text still gets the room it needs, then clamped to the screen.
    /// </summary>
    public (int Width, int Height) WindowSize
    {
        get
        {
            var width = BaseWidth * TextScale;
            var height = BaseHeight * TextScale;

            if (LogicalScreen is { } screen)
            {
                width = Math.Min(width, screen.Width * MaxScreenFraction);
                height = Math.Min(height, screen.Height * MaxScreenFraction);
            }

            // Never shrink below something usable, even on a very small screen.
            return ((int)Math.Round(Math.Max(width, 640)), (int)Math.Round(Math.Max(height, 400)));
        }
    }

    public static async Task<DisplayMetrics> ReadAsync(CancellationToken cancellationToken = default)
    {
        var textScaleTask = ReadTextScaleAsync(cancellationToken);
        var screenTask = ReadLogicalScreenAsync(cancellationToken);

        await Task.WhenAll(textScaleTask, screenTask).ConfigureAwait(false);

        return new DisplayMetrics(
            await textScaleTask.ConfigureAwait(false),
            await screenTask.ConfigureAwait(false));
    }

    /// <summary>
    /// Reads GNOME's text-scaling-factor, which is what the desktop's own
    /// "text size" control writes and every GTK app multiplies its fonts by.
    /// </summary>
    internal static async Task<double> ReadTextScaleAsync(CancellationToken cancellationToken = default)
    {
        // An explicit override for desktops that keep this setting elsewhere.
        var overridden = Environment.GetEnvironmentVariable("HOTKEYVIEWER_TEXT_SCALE");
        if (ParseScale(overridden) is { } explicitScale)
        {
            return explicitScale;
        }

        var result = await ProcessRunner
            .RunAsync("gsettings", ["get", "org.gnome.desktop.interface", "text-scaling-factor"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? ParseScale(result.StandardOutput) ?? 1.0 : 1.0;
    }

    /// <summary>Clamped so a stray setting cannot render the window unusable.</summary>
    internal static double? ParseScale(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
            double.IsNaN(scale) || scale <= 0)
        {
            return null;
        }

        return Math.Clamp(scale, 0.5, 3.0);
    }

    private static async Task<(double, double)?> ReadLogicalScreenAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessRunner
            .RunAsync("hyprctl", ["monitors", "-j"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);

            foreach (var monitor in document.RootElement.EnumerateArray())
            {
                if (!monitor.TryGetProperty("focused", out var focused) || !focused.GetBoolean())
                {
                    continue;
                }

                var scale = monitor.TryGetProperty("scale", out var s) ? s.GetDouble() : 1.0;
                if (scale <= 0)
                {
                    scale = 1.0;
                }

                // hyprctl reports the mode in physical pixels; windows are
                // positioned and sized in logical ones.
                return (monitor.GetProperty("width").GetDouble() / scale,
                        monitor.GetProperty("height").GetDouble() / scale);
            }
        }
        catch (JsonException)
        {
            // A malformed reply just means we size from the text scale alone.
        }

        return null;
    }
}
