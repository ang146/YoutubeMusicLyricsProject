using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using LyricsDisplayer.Resources;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

public sealed class ExternalLrcOpener
{
    private readonly Action<string> _launch;
    private readonly ILogger<ExternalLrcOpener> _logger;

    public ExternalLrcOpener(ILogger<ExternalLrcOpener> logger, Action<string>? launch = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _launch = launch ?? LaunchWithShell;
    }

    public bool TryOpen(string path, out string? error)
    {
        error = null;
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                error = Strings.LrcPathNotAbsolute;
                return false;
            }
            if (!File.Exists(path))
            {
                error = Strings.LrcFileUnavailable;
                return false;
            }

            _launch(path);
            _logger.LogInformation("Opened the current LRC with its default application.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                            InvalidOperationException or Win32Exception or ArgumentException or
                                            NotSupportedException or SecurityException)
        {
            error = string.Format(CultureInfo.CurrentCulture, Strings.LrcCouldNotOpenType,
                exception.GetType().Name);
            _logger.LogWarning(exception, "The current LRC could not be opened safely.");
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
