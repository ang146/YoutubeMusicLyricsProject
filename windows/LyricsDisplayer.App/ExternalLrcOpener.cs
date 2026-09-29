using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace LyricsDisplayer;

internal sealed class ExternalLrcOpener
{
    private readonly Action<string> _launch;
    private readonly Action<string, string, string>? _log;

    public ExternalLrcOpener(Action<string>? launch = null, Action<string, string, string>? log = null)
    {
        _launch = launch ?? LaunchWithShell;
        _log = log;
    }

    public bool TryOpen(string path, out string? error)
    {
        error = null;
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                error = "The local LRC path is not absolute.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "The current local LRC file is unavailable.";
                return false;
            }

            _launch(path);
            _log?.Invoke("Information", "ExternalLyrics", "Opened the current LRC with its default application.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                            InvalidOperationException or Win32Exception or ArgumentException or
                                            NotSupportedException or SecurityException)
        {
            error = $"The LRC could not be opened ({exception.GetType().Name}).";
            _log?.Invoke("Warning", "ExternalLyrics", error);
            return false;
        }
    }

    private static void LaunchWithShell(string path)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        if (process is null) throw new InvalidOperationException("The shell did not start an application for the LRC.");
        process.Dispose();
    }
}
