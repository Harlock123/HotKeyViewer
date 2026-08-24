namespace HotKeyViewer.Services;

/// <summary>
/// Asks the running compositor to open this window as a floating, centred
/// overlay instead of tiling it.
/// </summary>
/// <remarks>
/// Applied at runtime rather than written into the user's config: the rule
/// lasts only for the current compositor session, so the app never edits files
/// it does not own. A user who wants it permanent can put the same rule in
/// their own config — see the README.
/// </remarks>
public static class HyprlandWindowRule
{
    /// <summary>WM_CLASS this app reports under the X11 fallback backend.</summary>
    public const string WindowClass = "hotkeyviewer";

    /// <summary>
    /// The window title, which is the only thing a rule can match under the
    /// native Wayland backend: Avalonia.Wayland 12.1.1 never calls
    /// xdg_toplevel.set_app_id, so Hyprland reports an empty class for it.
    /// </summary>
    public const string WindowTitle = "Hyprland Hotkeys";

    /// <summary>
    /// Rules for a Lua-configured Hyprland, which rejects <c>hyprctl keyword</c>
    /// outright with "keyword can't work with non-legacy parsers".
    /// </summary>
    private static string[] LuaRules(int width, int height) =>
    [
        $$"""hl.window_rule({ match = { class = "{{WindowClass}}" }, float = true, center = true, size = { {{width}}, {{height}} } })""",
        $$"""hl.window_rule({ match = { title = "^{{WindowTitle}}$" }, float = true, center = true, size = { {{width}}, {{height}} } })""",
    ];

    /// <summary>Equivalent rules for a classic hyprlang config.</summary>
    private static string[] LegacyRules(int width, int height) =>
    [
        $"float, class:^({WindowClass})$",
        $"center, class:^({WindowClass})$",
        $"size {width} {height}, class:^({WindowClass})$",
        $"float, title:^({WindowTitle})$",
        $"center, title:^({WindowTitle})$",
        $"size {width} {height}, title:^({WindowTitle})$",
    ];

    private static bool IsHyprlandSession =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

    /// <summary>
    /// Registers the rule and waits for it to take effect. Must complete before
    /// the window is shown: a window rule only applies to windows mapped after
    /// it exists, so doing this in the background races the window opening and
    /// usually loses.
    /// </summary>
    public static async Task ApplyAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        if (!IsHyprlandSession)
        {
            return;
        }

        var applied = false;

        foreach (var rule in LuaRules(width, height))
        {
            var lua = await ProcessRunner.RunAsync("hyprctl", ["eval", rule], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            applied |= lua.Succeeded
                && lua.StandardOutput.Trim().StartsWith("ok", StringComparison.OrdinalIgnoreCase);
        }

        if (applied)
        {
            return;
        }

        foreach (var rule in LegacyRules(width, height))
        {
            await ProcessRunner.RunAsync("hyprctl", ["keyword", "windowrule", rule], cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
