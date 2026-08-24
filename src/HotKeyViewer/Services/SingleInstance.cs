using System.Diagnostics;

namespace HotKeyViewer.Services;

/// <summary>
/// Lets one keybinding both open and dismiss the window.
/// </summary>
/// <remarks>
/// Detection is by process rather than by window: this app owns exactly one
/// window and exits when it closes, so the process is the window. It also keeps
/// the check independent of the compositor — asking Hyprland would mean matching
/// on the title, since the Wayland backend leaves the app_id empty.
/// </remarks>
public static class SingleInstance
{
    /// <summary>
    /// Terminates any other running copy. Returns true when one was found, in
    /// which case the caller should exit instead of opening a second window.
    /// </summary>
    public static async Task<bool> CloseExistingAsync(CancellationToken cancellationToken = default)
    {
        var others = FindOthers();
        if (others.Count == 0)
        {
            return false;
        }

        foreach (var pid in others)
        {
            // SIGTERM rather than Process.Kill, which sends SIGKILL on Linux:
            // the graceful signal lets the toolkit tear the window down.
            await ProcessRunner.RunAsync("kill", ["-TERM", pid.ToString()], cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    private static List<int> FindOthers()
    {
        var current = Environment.ProcessId;
        var name = Process.GetCurrentProcess().ProcessName;
        var others = new List<int>();

        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.Id != current)
                {
                    others.Add(process.Id);
                }
            }
        }

        return others;
    }
}
