using System.Reflection;
using HotKeyViewer.Models;
using HotKeyViewer.Services;

namespace HotKeyViewer.Sources;

/// <summary>
/// Recovers bindings from a Lua-based Hyprland config by executing it against a
/// stub compositor API.
/// </summary>
/// <remarks>
/// Hyprland's Lua provider reports every Lua bind as dispatcher <c>__lua</c>
/// with an opaque numeric handle, so the live bind list can say <em>which</em>
/// chord is bound but never <em>what it runs</em>. The config also builds
/// bindings with loops and conditionals, so reading it as text would both miss
/// entries and invent ones that a disabled branch never created. Running it is
/// the only way to get the same set Hyprland itself got — and hooking the stub
/// lets us record the defining file and line for every bind, which is what
/// tells a user's own bindings apart from their distribution's.
/// </remarks>
public static class LuaConfigScanner
{
    private const string ScriptResource = "HotKeyViewer.Sources.scan-lua-config.lua";

    /// <summary>Lua entry points, in the order Hyprland itself would use.</summary>
    public static IEnumerable<string> CandidateEntryPoints(string configDirectory) =>
    [
        Path.Combine(configDirectory, "hyprland.lua"),
        Path.Combine(configDirectory, "init.lua"),
    ];

    public static async Task<ConfigScanResult> ScanAsync(
        string configDirectory,
        CancellationToken cancellationToken = default)
    {
        var entryPoint = CandidateEntryPoints(configDirectory).FirstOrDefault(File.Exists);
        if (entryPoint is null)
        {
            return ConfigScanResult.Empty;
        }

        string scriptPath;
        try
        {
            scriptPath = await ExtractScriptAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ConfigScanResult([], [], [$"Could not unpack the Lua scanner: {ex.Message}"]);
        }

        try
        {
            var interpreter = FindInterpreter();
            if (interpreter is null)
            {
                return new ConfigScanResult([], [], [
                    "No 'lua' interpreter found, so commands and source files for Lua-defined " +
                    "bindings are unavailable. Install lua to see them."
                ]);
            }

            var result = await ProcessRunner
                .RunAsync(interpreter, [scriptPath, entryPoint], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var warnings = new List<string>();

            // A config that throws part-way still emitted every bind before the
            // failure, so keep the partial output and surface the error.
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                warnings.Add(result.StandardError.Trim());
            }

            var (binds, files) = ParseRecords(result.StandardOutput, entryPoint);
            return new ConfigScanResult(binds, files, warnings);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    private static (List<ConfigBind> Binds, List<string> Files) ParseRecords(string output, string entryPoint)
    {
        var binds = new List<ConfigBind>();
        var files = new HashSet<string>(StringComparer.Ordinal) { entryPoint };

        foreach (var line in output.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 8)
            {
                continue;
            }

            var isUnbind = fields[0] == "unbind";
            if (!isUnbind && fields[0] != "bind")
            {
                continue;
            }

            _ = int.TryParse(fields[1], out var modmask);
            _ = int.TryParse(fields[7], out var sourceLine);

            var sourceFile = Unescape(fields[6]);
            if (sourceFile.Length > 0)
            {
                files.Add(sourceFile);
            }

            binds.Add(new ConfigBind
            {
                Chord = new KeyChord(modmask, Unescape(fields[2])),
                Description = Unescape(fields[3]),
                Kind = Unescape(fields[4]),
                Command = Unescape(fields[5]),
                SourceFile = sourceFile,
                SourceLine = sourceLine,
                IsUnbind = isUnbind,
            });
        }

        return (binds, [.. files]);
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            builder.Append(value[++i] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                var other => other,
            });
        }

        return builder.ToString();
    }

    private static string? FindInterpreter()
    {
        // Prefer the plain name so the config runs under the same interpreter
        // the system would otherwise use.
        string[] candidates = ["lua", "lua5.4", "lua5.3", "luajit"];
        var searchPaths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in candidates)
        {
            foreach (var directory in searchPaths)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static async Task<string> ExtractScriptAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResource)
            ?? throw new InvalidOperationException($"embedded resource {ScriptResource} is missing");

        var path = Path.Combine(Path.GetTempPath(), $"hotkeyviewer-scan-{Environment.ProcessId}.lua");

        await using (var file = File.Create(path))
        {
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
