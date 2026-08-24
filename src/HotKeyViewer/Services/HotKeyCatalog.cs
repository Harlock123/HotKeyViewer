using HotKeyViewer.Models;
using HotKeyViewer.Sources;

namespace HotKeyViewer.Services;

/// <summary>The fully merged view of the session's hotkeys.</summary>
public sealed record HotKeyCatalog(
    IReadOnlyList<HotKey> HotKeys,
    IReadOnlyList<string> FilesScanned,
    IReadOnlyList<string> Warnings)
{
    public int CustomCount => HotKeys.Count(k => k.IsCustom);

    /// <summary>
    /// Whether any binding came from outside the user's own config. False on a
    /// stock Hyprland install, where telling "yours" from "defaults" is
    /// meaningless because everything is yours.
    /// </summary>
    public bool HasDefaultsLayer { get; init; }

    public static readonly HotKeyCatalog Empty = new([], [], []);
}

/// <summary>
/// Builds the hotkey list by combining what the compositor currently has bound
/// with what the config files say about it.
/// </summary>
/// <remarks>
/// The two sources answer different halves of the question and neither is
/// sufficient alone. <c>hyprctl</c> is authoritative about which chords are
/// live right now — including anything added or remapped by the user — but for
/// a Lua config it cannot say what a binding does or where it came from. The
/// config scan supplies the command, the file, and the line, which is what makes
/// "this one is yours" and "this one overrides a default" answerable.
/// </remarks>
public static class HotKeyCatalogBuilder
{
    private static readonly Dictionary<string, string> CategoryByFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["applications.lua"] = "Applications",
        ["clipboard.lua"] = "Clipboard & Text",
        ["media.lua"] = "Media & Hardware",
        ["tiling.lua"] = "Windows & Workspaces",
        ["utilities.lua"] = "Utilities",
        ["voxtype.lua"] = "Dictation",
        ["toggles.lua"] = "Toggles",
    };

    public static async Task<HotKeyCatalog> BuildAsync(
        string? configDirectory = null,
        CancellationToken cancellationToken = default)
    {
        configDirectory ??= DefaultConfigDirectory();

        var keycodesTask = KeycodeResolver.LoadAsync(cancellationToken);
        var liveTask = HyprctlBindsReader.IsHyprlandRunning
            ? HyprctlBindsReader.ReadAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<LiveBind>>([]);
        var luaTask = LuaConfigScanner.ScanAsync(configDirectory, cancellationToken);

        // The .conf walk is synchronous file I/O; run it off the calling thread
        // so all four sources overlap.
        var confTask = Task.Run(() => ConfConfigScanner.Scan(configDirectory), cancellationToken);

        await Task.WhenAll(keycodesTask, liveTask, luaTask, confTask).ConfigureAwait(false);

        var keycodes = await keycodesTask.ConfigureAwait(false);
        var live = await liveTask.ConfigureAwait(false);
        var config = (await luaTask.ConfigureAwait(false)).Merge(await confTask.ConfigureAwait(false));

        var warnings = new List<string>(config.Warnings);

        if (live.Count == 0 && HyprctlBindsReader.IsHyprlandRunning)
        {
            warnings.Add("hyprctl returned no bindings; showing what the config files declare instead.");
        }

        var hotKeys = Merge(live, config, keycodes, configDirectory, warnings);

        return new HotKeyCatalog(hotKeys, config.FilesScanned, warnings)
        {
            HasDefaultsLayer = hotKeys.Any(k => k.Origin == BindOrigin.Default),
        };
    }

    public static string DefaultConfigDirectory()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(configHome, "hypr");
    }

    internal static List<HotKey> Merge(
        IReadOnlyList<LiveBind> live,
        ConfigScanResult config,
        KeycodeResolver keycodes,
        string configDirectory,
        List<string> warnings)
    {
        var declarations = config.Binds.Where(b => !b.IsUnbind).ToList();

        // A stock Hyprland install has no distribution layer: every binding is
        // in the user's own config. "Yours" only means something when there is
        // a set of defaults to contrast it against.
        var hasDefaultsLayer = declarations.Any(b => !IsUserFile(b.SourceFile, Path.GetFullPath(configDirectory)));

        // A chord can legitimately carry several bindings (Hyprland runs them
        // all), so matches are consumed from a queue rather than looked up once.
        var byChordAndDescription = BuildIndex(declarations, b => $"{b.Chord.MatchKey}|{b.Description.ToUpperInvariant()}");
        var byChord = BuildIndex(declarations, b => b.Chord.MatchKey);

        var userDirectory = Path.GetFullPath(configDirectory);
        var chordsFromDefaults = declarations
            .Where(b => !IsUserFile(b.SourceFile, userDirectory))
            .Select(b => b.Chord.MatchKey)
            .ToHashSet(StringComparer.Ordinal);
        var chordsFromUser = declarations
            .Where(b => IsUserFile(b.SourceFile, userDirectory))
            .Select(b => b.Chord.MatchKey)
            .ToHashSet(StringComparer.Ordinal);

        // An unbind in the user's own files means they deliberately switched a
        // default off, which makes whatever replaced it an override.
        foreach (var chord in config.Binds.Where(b => b.IsUnbind && IsUserFile(b.SourceFile, userDirectory)))
        {
            chordsFromUser.Add(chord.Chord.MatchKey);
        }

        // With no compositor to ask, the config declarations are the whole story.
        var records = live.Count > 0
            ? live
            : [.. declarations.Select(b => new LiveBind
            {
                Chord = b.Chord,
                RawKey = b.Chord.Key,
                Description = b.Description,
                Dispatcher = b.Kind,
                Arg = b.Command,
            })];

        var hotKeys = new List<HotKey>(records.Count);

        foreach (var bind in records)
        {
            var declaration =
                Take(byChordAndDescription, $"{bind.Chord.MatchKey}|{bind.Description.ToUpperInvariant()}")
                ?? Take(byChord, bind.Chord.MatchKey);

            var origin = declaration is null
                ? BindOrigin.Unknown
                : IsUserFile(declaration.SourceFile, userDirectory) ? BindOrigin.User : BindOrigin.Default;

            // Only a chord the defaults also claim counts as an override; a
            // brand-new user binding is an addition, not a remap.
            var isOverride = origin == BindOrigin.User
                && chordsFromDefaults.Contains(bind.Chord.MatchKey);

            var command = ResolveCommand(bind, declaration);
            var description = FirstNonEmpty(bind.Description, declaration?.Description, command, bind.Chord.Display);

            // Render code:10 as the key it actually prints, but keep matching on
            // the raw form so nothing depends on the active layout.
            var chord = bind.Chord with { Key = keycodes.Resolve(bind.Chord.Key) };

            hotKeys.Add(new HotKey
            {
                Chord = chord,
                Description = description,
                Command = command,
                Dispatcher = bind.Dispatcher,
                Submap = bind.Submap,
                Options = bind.Options,
                Origin = origin,
                SourceFile = declaration?.SourceFile ?? string.Empty,
                SourceLine = declaration?.SourceLine ?? 0,
                IsOverride = isOverride,
                Category = Categorise(declaration?.SourceFile, origin, description, command, bind.Submap, hasDefaultsLayer),
                SearchText = BuildSearchText(chord, description, command, declaration?.SourceFile),
            });
        }

        // Report chords the user replaced so the count in the UI is explainable.
        var replaced = chordsFromUser.Intersect(chordsFromDefaults, StringComparer.Ordinal).Count();
        if (replaced > 0)
        {
            warnings.Add($"{replaced} default binding(s) remapped by your own config.");
        }

        return hotKeys;
    }

    private static Dictionary<string, Queue<ConfigBind>> BuildIndex(
        IEnumerable<ConfigBind> binds,
        Func<ConfigBind, string> keySelector)
    {
        var index = new Dictionary<string, Queue<ConfigBind>>(StringComparer.Ordinal);

        foreach (var bind in binds)
        {
            var key = keySelector(bind);
            if (!index.TryGetValue(key, out var queue))
            {
                index[key] = queue = new Queue<ConfigBind>();
            }

            queue.Enqueue(bind);
        }

        return index;
    }

    private static ConfigBind? Take(Dictionary<string, Queue<ConfigBind>> index, string key) =>
        index.TryGetValue(key, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;

    private static string ResolveCommand(LiveBind bind, ConfigBind? declaration)
    {
        // "__lua" plus a handle number is all the compositor can say about a Lua
        // bind, so the config scan is the only source of a real command there.
        if (!string.IsNullOrEmpty(declaration?.Command))
        {
            return declaration.Command;
        }

        if (bind.Dispatcher is "__lua" or "")
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(bind.Arg) ? bind.Dispatcher : $"{bind.Dispatcher} {bind.Arg}";
    }

    private static bool IsUserFile(string path, string userDirectory) =>
        !string.IsNullOrEmpty(path)
        && Path.GetFullPath(path).StartsWith(userDirectory, StringComparison.Ordinal);

    private static string Categorise(
        string? sourceFile,
        BindOrigin origin,
        string description,
        string command,
        string submap,
        bool hasDefaultsLayer)
    {
        // A submap binding only fires inside that mode, so it belongs in its own
        // section rather than mixed in with the always-live ones.
        if (!string.IsNullOrEmpty(submap))
        {
            return $"Submap: {submap}";
        }

        // Singling the user's bindings out is only useful when there are
        // defaults to single them out from. On a stock install it would put
        // every binding in one bucket.
        if (hasDefaultsLayer && origin == BindOrigin.User)
        {
            return "Your bindings";
        }

        if (origin == BindOrigin.Default && !string.IsNullOrEmpty(sourceFile))
        {
            var file = Path.GetFileName(sourceFile);
            return CategoryByFile.TryGetValue(file, out var mapped)
                ? mapped
                : Humanise(Path.GetFileNameWithoutExtension(file));
        }

        // Nothing else groups these: on a stock install they all sit in one
        // hyprland.conf, so what the binding does is the only useful axis.
        return FromBehaviour(description, command);
    }

    private static string FromBehaviour(string description, string command)
    {
        var text = $"{description} {command}";

        bool Mentions(params string[] words) =>
            words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

        if (Mentions("workspace", "window", "focus", "monitor", "fullscreen", "float", "tile", "resize", "swap", "group"))
        {
            return "Windows & Workspaces";
        }

        if (Mentions("volume", "brightness", "mute", "audio", "media", "play", "pause", "XF86"))
        {
            return "Media & Hardware";
        }

        if (Mentions("screenshot", "lock", "exit", "reload", "kill", "power", "suspend", "quit"))
        {
            return "System";
        }

        if (Mentions("exec", "launch", "terminal", "browser", "menu", "run"))
        {
            return "Applications";
        }

        return "Other";
    }

    private static string Humanise(string name)
    {
        var spaced = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return spaced.Length == 0 ? "Other" : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string BuildSearchText(KeyChord chord, string description, string command, string? sourceFile) =>
        string.Join(' ', chord.Display, description, command, Path.GetFileName(sourceFile ?? string.Empty))
            .ToLowerInvariant();
}
