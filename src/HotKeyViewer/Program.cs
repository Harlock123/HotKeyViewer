using Avalonia;
using Avalonia.Wayland;
using HotKeyViewer.Services;

namespace HotKeyViewer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        // Toggle: one key both opens and dismisses the window.
        if (args.Contains("--toggle") &&
            SingleInstance.CloseExistingAsync().GetAwaiter().GetResult())
        {
            return 0;
        }

        if (args.Contains("--debug-keycodes"))
        {
            return DebugKeycodesAsync().GetAwaiter().GetResult();
        }

        // A text mode keeps the tool usable over SSH and makes the whole data
        // pipeline testable without a display.
        if (args.Contains("--print") || args.Contains("-p"))
        {
            return PrintAsync().GetAwaiter().GetResult();
        }

        // Both the window rule and the UI need these, and the rule has to be
        // registered before the window maps — a rule only applies to windows
        // opened after it exists — so this blocks rather than running in the
        // background. It is a couple of fast IPC round trips.
        App.Metrics = DisplayMetrics.ReadAsync().GetAwaiter().GetResult();

        var (width, height) = App.Metrics.WindowSize;
        HyprlandWindowRule.ApplyAsync(width, height).GetAwaiter().GetResult();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // Wayland first, X11 second. Going through XWayland on a fractionally
            // scaled output (this laptop runs 1.6) means the compositor upscales
            // a 1x surface, so text renders blurry and at the wrong size; a native
            // Wayland surface is told the real fractional scale instead.
            // UsePlatformDetect supplies the X11 fallback that must exist first.
            .UsePlatformDetect()
            .UseWaylandWithFallback()
            .WithInterFont()
            .LogToTrace();

    private static void PrintUsage() =>
        Console.WriteLine("""
            hotkeyviewer — show every hotkey Hyprland currently has bound.

            Usage:
              hotkeyviewer            Open the floating hotkey window.
              hotkeyviewer --toggle   Open the window, or close it if it is open.
              hotkeyviewer --print    List the bindings as text and exit.
              hotkeyviewer --help     Show this message.

            Diagnostics:
              --debug-keycodes        Show how raw key tokens resolve on this keyboard.
            """);

    private static async Task<int> DebugKeycodesAsync()
    {
        var resolver = await Sources.KeycodeResolver.LoadAsync();
        foreach (var code in new[] { "code:10", "code:20", "code:34", "code:35", "code:201", "mouse_up" })
        {
            Console.WriteLine($"  {code,-12} -> {resolver.Resolve(code)}");
        }

        return 0;
    }

    private static async Task<int> PrintAsync()
    {
        var catalog = await HotKeyCatalogBuilder.BuildAsync().ConfigureAwait(false);

        foreach (var group in catalog.HotKeys.GroupBy(k => k.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine();
            Console.WriteLine($"── {group.Key} ──");

            foreach (var hotKey in group.OrderBy(k => k.Description, StringComparer.OrdinalIgnoreCase))
            {
                var badge = hotKey.IsOverride ? " [remapped]" : hotKey.Origin == Models.BindOrigin.User ? " [yours]" : string.Empty;
                Console.WriteLine($"  {hotKey.Chord.Display,-34} → {hotKey.Description}{badge}");

                if (hotKey.HasCommand)
                {
                    Console.WriteLine($"  {"",-34}   {hotKey.Command}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{catalog.HotKeys.Count} bindings · {catalog.CustomCount} defined or remapped by you");
        Console.WriteLine($"{catalog.FilesScanned.Count} config files scanned");

        foreach (var warning in catalog.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        return catalog.HotKeys.Count > 0 ? 0 : 1;
    }
}
