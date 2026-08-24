using System.Diagnostics;
using System.Text;

namespace HotKeyViewer.Services;

public readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Thin wrapper for the external tools this app reads from.</summary>
public static class ProcessRunner
{
    /// <summary>
    /// Runs a command and captures its output. Never throws for a missing
    /// binary or a non-zero exit: every source this app reads is optional, and
    /// a missing one should degrade the view rather than kill the app.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(-1, string.Empty, $"could not start {fileName}");
            }

            // Read both pipes concurrently; a tool that fills one while we block
            // on the other would deadlock.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new ProcessResult(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }
    }

    /// <summary>Fire-and-forget for commands whose output we do not need.</summary>
    public static void RunDetached(string fileName, IEnumerable<string> arguments)
    {
        _ = RunAsync(fileName, arguments);
    }
}
