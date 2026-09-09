using System.Buffers.Binary;
using System.Text;

namespace LyricsDisplayer.NativeHost.Tests;

[TestFixture]
public sealed class NativeMessagingReaderTests
{
    [Test]
    public async Task ReadsLittleEndianLengthPrefixedUtf8Frame()
    {
        const string json = "{\"protocolVersion\":1,\"message\":\"測試\"}";
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length);
        body.CopyTo(frame, 4);
        var reader = new NativeMessagingReader(new MemoryStream(frame));

        var result = await reader.ReadMessageAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(json));
    }

    [Test]
    public async Task CleanEofReturnsNull()
    {
        var reader = new NativeMessagingReader(new MemoryStream());
        Assert.That(await reader.ReadMessageAsync(CancellationToken.None), Is.Null);
    }

    [Test]
    public void TruncatedBodyIsRejected()
    {
        var frame = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 20);
        var reader = new NativeMessagingReader(new MemoryStream(frame));
        Assert.That(async () => await reader.ReadMessageAsync(CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
    }
}

