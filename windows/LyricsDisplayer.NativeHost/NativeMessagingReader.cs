using System.Buffers.Binary;
using System.Text;

namespace LyricsDisplayer.NativeHost;

public sealed class NativeMessagingReader(Stream input, int maximumMessageBytes = 1024 * 1024)
{
    public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(uint)];
        var headerBytes = await ReadExactlyOrEofAsync(input, header, cancellationToken);
        if (headerBytes == 0)
        {
            return null;
        }

        if (headerBytes != header.Length)
        {
            throw new InvalidDataException("Firefox Native Messaging ended during a four-byte length header.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > maximumMessageBytes)
        {
            throw new InvalidDataException($"Firefox Native Messaging frame length {length} is outside the supported range.");
        }

        var body = new byte[length];
        var bodyBytes = await ReadExactlyOrEofAsync(input, body, cancellationToken);
        if (bodyBytes != body.Length)
        {
            throw new InvalidDataException("Firefox Native Messaging ended during a message body.");
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(body);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Firefox Native Messaging body was not valid UTF-8.", exception);
        }
    }

    private static async Task<int> ReadExactlyOrEofAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (count == 0)
            {
                break;
            }

            total += count;
        }

        return total;
    }
}

