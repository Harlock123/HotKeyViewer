using System.Text;
using HotKeyViewer.Models;

namespace HotKeyViewer.Services;

/// <summary>How a binding will be removed.</summary>
public enum RemovalKind
{
    /// <summary>Comment out the line that defines it, in a file the user owns.</summary>
    CommentOut,

    /// <summary>Append an override that switches it off, leaving the definition alone.</summary>
    Unbind,

    /// <summary>Nothing safe can be done.</summary>
    Unsupported,
}

/// <summary>
/// What removing a binding will do, worked out before anything is written so the
/// user can be shown the exact edit and the reason for it.
/// </summary>
public sealed record RemovalPlan
{
    public required RemovalKind Kind { get; init; }

    /// <summary>The file that will be modified.</summary>
    public string TargetFile { get; init; } = string.Empty;

    /// <summary>The line that will be commented out, for <see cref="RemovalKind.CommentOut"/>.</summary>
    public int TargetLine { get; init; }

    /// <summary>The exact text that will be appended, for <see cref="RemovalKind.Unbind"/>.</summary>
    public string TextToAppend { get; init; } = string.Empty;

    /// <summary>One sentence explaining why this approach was chosen.</summary>
    public required string Explanation { get; init; }

    public bool CanApply => Kind != RemovalKind.Unsupported;
}

public sealed record RemovalResult(bool Succeeded, string Message, string? BackupFile = null);

/// <summary>
/// Removes a keybinding by editing only files the user owns.
/// </summary>
/// <remarks>
/// Deleting the definition is almost never right. Most bindings live under the
/// distribution's directory, where an edit is reverted by the next update, and
/// many are produced by loops — one line in Omarchy's tiling.lua makes ten
/// workspace bindings, so removing that line would take out all ten. The
/// supported mechanism is an override in the user's own config, which works per
/// chord and is immune to both problems. Commenting the definition out is
/// offered only when it is the user's own file and that line makes exactly one
/// binding.
/// </remarks>
public static class BindingRemover
{
    public static RemovalPlan Plan(HotKey hotKey, string configDirectory, bool isLuaConfig)
    {
        var chord = FormatChord(hotKey.RawChord, isLuaConfig);
        if (string.IsNullOrWhiteSpace(chord))
        {
            return new RemovalPlan
            {
                Kind = RemovalKind.Unsupported,
                Explanation = "This binding has no chord that can be named in an override.",
            };
        }

        var overrideFile = OverrideFile(configDirectory, isLuaConfig);
        var ownedByUser = !string.IsNullOrEmpty(hotKey.SourceFile)
            && Path.GetFullPath(hotKey.SourceFile)
                .StartsWith(Path.GetFullPath(configDirectory), StringComparison.Ordinal);

        // The definition can only be removed when it is the user's own and that
        // line is responsible for this binding alone.
        if (ownedByUser && hotKey.DefinitionShareCount == 1 && hotKey.SourceLine > 0)
        {
            return new RemovalPlan
            {
                Kind = RemovalKind.CommentOut,
                TargetFile = hotKey.SourceFile,
                TargetLine = hotKey.SourceLine,
                Explanation = "This is your own binding, so its definition is commented out.",
            };
        }

        var reason = !ownedByUser
            ? "This binding comes from your distribution, so its own file is left alone — edits there are lost on update."
            : $"The line that defines it also defines {hotKey.DefinitionShareCount - 1} other binding(s), so it cannot be removed on its own.";

        return new RemovalPlan
        {
            Kind = RemovalKind.Unbind,
            TargetFile = overrideFile,
            TextToAppend = isLuaConfig
                ? $"hl.unbind(\"{chord}\")"
                : $"unbind = {chord}",
            Explanation = $"{reason} An override is added to {Path.GetFileName(overrideFile)} instead.",
        };
    }

    /// <summary>
    /// Applies the plan, then asks the compositor whether the config still
    /// parses. A backup is taken first and restored if anything goes wrong, so a
    /// failed edit cannot leave a broken window manager behind.
    /// </summary>
    public static async Task<RemovalResult> ApplyAsync(
        RemovalPlan plan,
        HotKey hotKey,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanApply)
        {
            return new RemovalResult(false, plan.Explanation);
        }

        string backup;
        try
        {
            backup = CreateBackup(plan.TargetFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RemovalResult(false, $"Could not back up {plan.TargetFile}: {ex.Message}");
        }

        try
        {
            if (plan.Kind == RemovalKind.CommentOut)
            {
                CommentOutLine(plan.TargetFile, plan.TargetLine);
            }
            else
            {
                AppendOverride(plan.TargetFile, plan.TextToAppend, hotKey);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Restore(backup, plan.TargetFile);
            return new RemovalResult(false, $"Could not edit {plan.TargetFile}: {ex.Message}");
        }

        var errors = await ReloadAndCheckAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(errors))
        {
            Restore(backup, plan.TargetFile);
            await ReloadAndCheckAsync(cancellationToken).ConfigureAwait(false);

            return new RemovalResult(false, $"Config rejected the change, so it was rolled back:\n{errors}");
        }

        return new RemovalResult(
            true,
            plan.Kind == RemovalKind.CommentOut
                ? $"Commented out {Path.GetFileName(plan.TargetFile)}:{plan.TargetLine}."
                : $"Added {plan.TextToAppend} to {Path.GetFileName(plan.TargetFile)}.",
            backup);
    }

    /// <summary>Which file overrides belong in, per config flavour.</summary>
    public static string OverrideFile(string configDirectory, bool isLuaConfig)
    {
        if (!isLuaConfig)
        {
            return Path.Combine(configDirectory, "hyprland.conf");
        }

        // Omarchy's layout keeps personal bindings here; fall back to the entry
        // point when the split file is absent.
        var bindings = Path.Combine(configDirectory, "bindings.lua");
        return File.Exists(bindings) ? bindings : Path.Combine(configDirectory, "hyprland.lua");
    }

    /// <summary>
    /// A chord spelled the way the config expects, built from the raw key so a
    /// keycode binding stays a keycode binding.
    /// </summary>
    internal static string FormatChord(KeyChord chord, bool isLuaConfig)
    {
        if (string.IsNullOrEmpty(chord.Key))
        {
            return string.Empty;
        }

        return isLuaConfig
            ? string.Join(" + ", chord.Parts)
            // hyprlang wants the modifiers run together, then the key.
            : $"{string.Join(" ", chord.Modifiers)}, {chord.Key}".TrimStart(',', ' ');
    }

    internal static string CommentPrefix(string file) =>
        file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? "-- " : "# ";

    private static void CommentOutLine(string file, int line)
    {
        var lines = File.ReadAllLines(file);
        var index = line - 1;

        if (index < 0 || index >= lines.Length)
        {
            throw new IOException($"{file} has no line {line}; the file changed since it was read.");
        }

        lines[index] = CommentPrefix(file) + lines[index];
        File.WriteAllLines(file, lines);
    }

    private static void AppendOverride(string file, string text, HotKey hotKey)
    {
        var builder = new StringBuilder();

        if (File.Exists(file) && File.ReadAllText(file) is { Length: > 0 } existing && !existing.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        // A bare unbind is unreadable a month later, so record what it turned off.
        builder.Append('\n')
            .Append(CommentPrefix(file))
            .Append("Disabled by hotkeyviewer: ")
            .Append(hotKey.Chord.Display)
            .Append(" — ")
            .Append(hotKey.Description)
            .Append('\n')
            .Append(text)
            .Append('\n');

        File.AppendAllText(file, builder.ToString());
    }

    private static string CreateBackup(string file)
    {
        var backup = $"{file}.bak.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        File.Copy(file, backup, overwrite: true);
        return backup;
    }

    private static void Restore(string backup, string file)
    {
        try
        {
            File.Copy(backup, file, overwrite: true);
        }
        catch (IOException)
        {
            // The backup stays on disk either way, which is the point of it.
        }
    }

    /// <summary>Reloads the config and returns whatever the compositor complains about.</summary>
    private static async Task<string> ReloadAndCheckAsync(CancellationToken cancellationToken)
    {
        await ProcessRunner.RunAsync("hyprctl", ["reload"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = await ProcessRunner.RunAsync("hyprctl", ["configerrors"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var output = result.StandardOutput.Trim();

        // Hyprland prints "no errors" or an empty body when the config is clean.
        return output.Length == 0 || output.Contains("no errors", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : output;
    }
}
