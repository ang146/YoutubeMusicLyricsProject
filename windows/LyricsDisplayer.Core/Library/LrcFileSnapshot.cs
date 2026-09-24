using System.Security.Cryptography;
using System.Text;

namespace LyricsDisplayer.Core.Library;

internal sealed record LrcFileSnapshot(byte[] Bytes, string Content, string Hash, Encoding TextEncoding)
{
    public static LrcFileSnapshot Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        reader.ReadToEnd();
        var encoding = (Encoding)reader.CurrentEncoding.Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        var preamble = encoding.GetPreamble();
        var prefixLength = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        // Reject malformed bytes instead of silently changing unrelated text during a later token edit.
        var content = encoding.GetString(bytes, prefixLength, bytes.Length - prefixLength);
        return new(bytes, content, HashBytes(bytes), encoding);
    }

    public static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public byte[] Encode(string content)
    {
        var preamble = TextEncoding.GetPreamble();
        var bytes = TextEncoding.GetBytes(content);
        return preamble.Length > 0 && Bytes.AsSpan().StartsWith(preamble)
            ? [.. preamble, .. bytes]
            : bytes;
    }

    public static byte[] EncodeUtf8(string content, bool bom) =>
        bom ? [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(content)] : Encoding.UTF8.GetBytes(content);

    public static bool IsWritable(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
