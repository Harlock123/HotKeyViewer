using HotKeyViewer.Models;

namespace HotKeyViewer.Sources;

/// <summary>
/// Walks a classic hyprlang (<c>.conf</c>) Hyprland config, following every
/// <c>source =</c> include, and collects the bindings it declares.
/// </summary>
/// <remarks>
/// This is the path for a stock Hyprland install. Unlike the Lua config, a
/// hyprlang config is fully declarative, so reading it as text is both safe and
/// complete: it has no loops or conditionals that could change which bindings
/// exist. Variables are the one indirection, and they are expanded here.
/// </remarks>
public static class ConfConfigScanner
{
    private const int MaxIncludeDepth = 24;

    public static IEnumerable<string> CandidateEntryPoints(string configDirectory) =>
    [
        Path.Combine(configDirectory, "hyprland.conf"),
    ];

    public static ConfigScanResult Scan(string configDirectory)
    {
        var entryPoint = CandidateEntryPoints(configDirectory).FirstOrDefault(File.Exists);
        if (entryPoint is null)
        {
            return ConfigScanResult.Empty;
        }

        var state = new ScanState(configDirectory);
        ScanFile(entryPoint, state, depth: 0);

        return new ConfigScanResult(state.Binds, [.. state.Files], state.Warnings);
    }

    private sealed class ScanState(string configDirectory)
    {
        public string ConfigDirectory { get; } = configDirectory;
        public List<ConfigBind> Binds { get; } = [];
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        public List<string> Warnings { get; } = [];

        /// <summary>hyprlang <c>$name = value</c> definitions, expanded on use.</summary>
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
    }

    private static void ScanFile(string path, ScanState state, int depth)
    {
        // Guards both runaway include chains and a config that sources itself.
        if (depth > MaxIncludeDepth || !state.Files.Add(Path.GetFullPath(path)))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            state.Warnings.Add($"Could not read {path}: {ex.Message}");
            return;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var keyword = line[..equals].Trim();
            var value = Expand(line[(equals + 1)..].Trim(), state.Variables);

            if (keyword.StartsWith('$'))
            {
                state.Variables[keyword] = value;
            }
            else if (keyword.Equals("source", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var included in ResolveInclude(value, path, state))
                {
                    ScanFile(included, state, depth + 1);
                }
            }
            else if (keyword.Equals("unbind", StringComparison.OrdinalIgnoreCase))
            {
                state.Binds.Add(new ConfigBind
                {
                    Chord = ParseChord(value),
                    SourceFile = path,
                    SourceLine = index + 1,
                    IsUnbind = true,
                });
            }
            else if (IsBindKeyword(keyword, out var hasDescription))
            {
                if (ParseBind(value, hasDescription, path, index + 1) is { } bind)
                {
                    state.Binds.Add(bind);
                }
            }
        }
    }

    /// <summary>Matches bind, bindm, binde, bindd, bindde and every other flag mix.</summary>
    private static bool IsBindKeyword(string keyword, out bool hasDescription)
    {
        hasDescription = false;

        if (!keyword.StartsWith("bind", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var flags = keyword[4..];
        if (!flags.All(c => "lremntipd".Contains(char.ToLowerInvariant(c))))
        {
            return false;
        }

        hasDescription = flags.Contains('d', StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static ConfigBind? ParseBind(string value, bool hasDescription, string path, int line)
    {
        // Split only the leading fixed fields: the dispatcher argument may itself
        // contain commas and must survive intact.
        var maxFields = hasDescription ? 5 : 4;
        var fields = value.Split(',', maxFields);
        if (fields.Length < 3)
        {
            return null;
        }

        var chord = ParseChord($"{fields[0]},{fields[1]}");
        var offset = hasDescription ? 1 : 0;
        var description = hasDescription ? fields[2].Trim() : string.Empty;
        var dispatcher = fields.Length > 2 + offset ? fields[2 + offset].Trim() : string.Empty;
        var argument = fields.Length > 3 + offset ? fields[3 + offset].Trim() : string.Empty;

        var isExec = dispatcher.StartsWith("exec", StringComparison.OrdinalIgnoreCase);

        return new ConfigBind
        {
            Chord = chord,
            Description = description,
            Kind = isExec ? "exec" : "dispatch",
            Command = isExec
                ? argument
                : (argument.Length > 0 ? $"{dispatcher} {argument}" : dispatcher),
            SourceFile = path,
            SourceLine = line,
        };
    }

    /// <summary>Parses the "MODS, KEY" head of a bind line.</summary>
    private static KeyChord ParseChord(string value)
    {
        var parts = value.Split(',', 2);
        var modifiers = parts[0].Trim();
        var key = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        // hyprlang writes modifiers run together ("SUPER SHIFT" or "SUPERSHIFT");
        // KeyChord.Parse wants them separated.
        var chord = KeyChord.Parse(modifiers.Replace(' ', '+'));
        return new KeyChord(chord.ModMask, key);
    }

    private static IEnumerable<string> ResolveInclude(string value, string currentFile, ScanState state)
    {
        var raw = value.Trim().Trim('"', '\'');
        if (raw.Length == 0)
        {
            yield break;
        }

        if (raw.StartsWith('~'))
        {
            raw = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                raw.TrimStart('~', '/'));
        }
        else if (!Path.IsPathRooted(raw))
        {
            raw = Path.Combine(Path.GetDirectoryName(currentFile) ?? state.ConfigDirectory, raw);
        }

        // hyprlang allows a glob, which is how many configs pull in a whole
        // conf.d directory.
        if (raw.Contains('*', StringComparison.Ordinal) || raw.Contains('?', StringComparison.Ordinal))
        {
            var directory = Path.GetDirectoryName(raw);
            var pattern = Path.GetFileName(raw);

            if (directory is null || !Directory.Exists(directory))
            {
                yield break;
            }

            foreach (var match in Directory.EnumerateFiles(directory, pattern).Order(StringComparer.Ordinal))
            {
                yield return match;
            }

            yield break;
        }

        if (File.Exists(raw))
        {
            yield return raw;
        }
        else
        {
            state.Warnings.Add($"{Path.GetFileName(currentFile)} sources a missing file: {raw}");
        }
    }

    private static string Expand(string value, Dictionary<string, string> variables)
    {
        if (!value.Contains('$', StringComparison.Ordinal) || variables.Count == 0)
        {
            return value;
        }

        // Longest name first, so $mainModShift is not clobbered by $mainMod.
        foreach (var (name, replacement) in variables.OrderByDescending(v => v.Key.Length))
        {
            value = value.Replace(name, replacement, StringComparison.Ordinal);
        }

        return value;
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash >= 0 ? line[..hash] : line;
    }
}
