namespace HotKeyViewer.Services;

/// <summary>
/// Opens a config file in the desktop's configured editor, positioned on the
/// line that defines a binding.
/// </summary>
/// <remarks>
/// Every editor spells "go to line" differently, and there is no portable form,
/// so the editor has to be identified before the arguments can be built. The
/// launch itself is delegated to omarchy-launch-editor where it exists, because
/// it already knows whether the chosen editor needs a terminal wrapped around it.
/// </remarks>
public static class EditorLauncher
{
    private const string OmarchyLauncher = "omarchy-launch-editor";

    /// <summary>Editors that need a terminal, used only for the fallback path.</summary>
    private static readonly HashSet<string> TerminalEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvim", "vim", "vi", "nano", "micro", "hx", "helix", "emacs", "kak",
    };

    /// <summary>
    /// The editor Omarchy records as the default, falling back to the standard
    /// environment variables.
    /// </summary>
    public static string ResolveEditor()
    {
        var stateFile = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
            "omarchy", "defaults", "editor");

        try
        {
            if (File.Exists(stateFile))
            {
                var recorded = File.ReadLines(stateFile).FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(recorded))
                {
                    return recorded;
                }
            }
        }
        catch (IOException)
        {
            // Fall through to the environment.
        }

        foreach (var variable in (string[])["VISUAL", "EDITOR"])
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // $EDITOR is itself "omarchy-launch-editor --inline" on Omarchy;
            // taking that literally would ask the launcher to edit with itself.
            var command = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (!Path.GetFileName(command).Equals(OmarchyLauncher, StringComparison.OrdinalIgnoreCase))
            {
                return command;
            }
        }

        return "nvim";
    }

    /// <summary>
    /// The arguments that open <paramref name="file"/> at <paramref name="line"/>
    /// for a given editor. Falls back to opening the file when the editor is
    /// unknown or no line is known.
    /// </summary>
    public static string[] ArgumentsFor(string editor, string file, int line)
    {
        if (line <= 0)
        {
            return [file];
        }

        return Path.GetFileName(editor).ToLowerInvariant() switch
        {
            "code" or "code-insiders" or "codium" or "vscodium" or "cursor" => ["-g", $"{file}:{line}"],
            "zed" or "zeditor" or "hx" or "helix" or "kak" => [$"{file}:{line}"],
            "nvim" or "vim" or "vi" or "nano" or "micro" or "emacs" => [$"+{line}", file],
            "rider" or "idea" or "clion" or "pycharm" or "webstorm" => ["--line", line.ToString(), file],
            "subl" or "sublime_text" => [$"{file}:{line}"],
            // Unknown editor: opening the right file is still most of the value.
            _ => [file],
        };
    }

    /// <summary>Opens the file, positioned on the line, without blocking the UI.</summary>
    public static void Open(string file, int line)
    {
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        var editor = ResolveEditor();
        var arguments = ArgumentsFor(editor, file, line);

        // The launcher picks the same editor from the same state file, and knows
        // whether to wrap it in a terminal.
        if (IsOnPath(OmarchyLauncher))
        {
            ProcessRunner.RunDetached(OmarchyLauncher, arguments);
            return;
        }

        if (TerminalEditors.Contains(Path.GetFileName(editor)) && IsOnPath("xdg-terminal-exec"))
        {
            ProcessRunner.RunDetached("xdg-terminal-exec", [editor, .. arguments]);
            return;
        }

        ProcessRunner.RunDetached(editor, arguments);
    }

    private static bool IsOnPath(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, command)));
}
