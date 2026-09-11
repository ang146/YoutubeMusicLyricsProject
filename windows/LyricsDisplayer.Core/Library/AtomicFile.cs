using System.Text;

namespace LyricsDisplayer.Core.Library;

internal static class AtomicFile
{
    public static void WriteNew(string path, string content)
    {
        Write(path, content, overwrite: false);
    }

    public static void Replace(string path, string content)
    {
        Write(path, content, overwrite: true);
    }

    private static void Write(string path, string content, bool overwrite)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A parent directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Never mask the original write failure with best-effort cleanup of our own temporary file.
            }
        }
    }
}
