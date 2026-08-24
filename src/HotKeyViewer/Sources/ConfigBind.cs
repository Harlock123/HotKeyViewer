using HotKeyViewer.Models;

namespace HotKeyViewer.Sources;

/// <summary>A binding as declared by a config file, with where it was declared.</summary>
public sealed record ConfigBind
{
    public required KeyChord Chord { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>"exec" for a shell command, "lua" for a dispatcher call.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The command line or the dispatcher expression.</summary>
    public string Command { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;
    public int SourceLine { get; init; }

    /// <summary>True for an explicit unbind of a previously declared chord.</summary>
    public bool IsUnbind { get; init; }
}

/// <summary>What one config walk produced, including any problems worth showing.</summary>
public sealed record ConfigScanResult(
    IReadOnlyList<ConfigBind> Binds,
    IReadOnlyList<string> FilesScanned,
    IReadOnlyList<string> Warnings)
{
    public static readonly ConfigScanResult Empty = new([], [], []);

    public ConfigScanResult Merge(ConfigScanResult other) => new(
        [.. Binds, .. other.Binds],
        [.. FilesScanned, .. other.FilesScanned],
        [.. Warnings, .. other.Warnings]);
}
