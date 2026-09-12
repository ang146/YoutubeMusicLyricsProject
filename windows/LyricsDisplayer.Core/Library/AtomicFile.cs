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

    internal static void ReplacePair(
        string firstPath,
        string firstContent,
        string secondPath,
        string secondContent,
        Action<int>? beforeCommit = null)
    {
        var originalFirst = File.ReadAllBytes(firstPath);
        var preserveUtf8Bom = originalFirst.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var firstTemporary = WriteTemporary(firstPath, firstContent, preserveUtf8Bom);
        string? secondTemporary = null;
        var firstCommitted = false;
        try
        {
            secondTemporary = WriteTemporary(secondPath, secondContent);
            beforeCommit?.Invoke(1);
            File.Move(firstTemporary, firstPath, overwrite: true);
            firstTemporary = string.Empty;
            firstCommitted = true;
            beforeCommit?.Invoke(2);
            File.Move(secondTemporary, secondPath, overwrite: true);
            secondTemporary = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (firstCommitted)
            {
                try
                {
                    ReplaceBytes(firstPath, originalFirst);
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("Coordinated file replacement failed and the first-file rollback also failed.",
                        new AggregateException(exception, rollbackException));
                }
            }
            throw new IOException("Coordinated file replacement failed; committed changes were rolled back.", exception);
        }
        finally
        {
            DeleteOwnTemporary(firstTemporary);
            DeleteOwnTemporary(secondTemporary);
        }
    }

    private static void Write(string path, string content, bool overwrite)
    {
        var temporary = WriteTemporary(path, content);
        try
        {
            File.Move(temporary, path, overwrite);
            temporary = string.Empty;
        }
        finally
        {
            DeleteOwnTemporary(temporary);
        }
    }

    private static void ReplaceBytes(string path, byte[] content)
    {
        var temporary = WriteTemporary(path, content);
        try
        {
            File.Move(temporary, path, overwrite: true);
            temporary = string.Empty;
        }
        finally
        {
            DeleteOwnTemporary(temporary);
        }
    }

    private static string WriteTemporary(string path, string content, bool writeUtf8Bom = false)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A parent directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(writeUtf8Bom)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
            return temporary;
        }
        catch
        {
            DeleteOwnTemporary(temporary);
            throw;
        }
    }

    private static string WriteTemporary(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A parent directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough);
            stream.Write(content);
            stream.Flush(true);
            return temporary;
        }
        catch
        {
            DeleteOwnTemporary(temporary);
            throw;
        }
    }

    private static void DeleteOwnTemporary(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Never mask the original write failure with best-effort cleanup of our own temporary file.
        }
    }
}
