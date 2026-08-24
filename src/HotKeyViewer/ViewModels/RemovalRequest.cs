using HotKeyViewer.Models;
using HotKeyViewer.Services;

namespace HotKeyViewer.ViewModels;

/// <summary>
/// A pending removal awaiting confirmation, with everything the prompt shows.
/// </summary>
/// <remarks>
/// Presented as an overlay inside the main window rather than a dialog window.
/// A Wayland client cannot give itself keyboard focus without an activation
/// token, and Avalonia's Wayland backend does not implement <c>Activate</c>, so
/// a separate toplevel opens unfocused and its buttons never receive the
/// keystrokes meant for them. Staying inside the focused toplevel avoids the
/// problem entirely.
/// </remarks>
public sealed record RemovalRequest(HotKey HotKey, RemovalPlan Plan, string Preview)
{
    public string Heading => $"Remove {HotKey.Chord.Display} — {HotKey.Description}?";

    public string Explanation => Plan.Explanation;

    public string Target => Plan.Kind == RemovalKind.CommentOut
        ? $"Comments out {Plan.TargetFile}:{Plan.TargetLine}"
        : $"Appends to {Plan.TargetFile}";
}
