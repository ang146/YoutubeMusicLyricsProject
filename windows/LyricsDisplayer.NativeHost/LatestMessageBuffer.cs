using System.Threading.Channels;

namespace LyricsDisplayer.NativeHost;

// Transport-only coalescing: retain one message per configured application type.
// Notifications may collapse; application payloads cannot evict a different type.
public sealed class LatestMessageBuffer(params string[] messageTypes)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _latest = new();
    private readonly HashSet<string> _dirty = new();
    private readonly Channel<string> _notifications = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<string> Reader => _notifications.Reader;

    public void Publish(string messageType, string json)
    {
        if (!messageTypes.Contains(messageType)) throw new ArgumentException("Unsupported application type.");
        lock (_gate)
        {
            _latest[messageType] = json;
            _dirty.Add(messageType);
            _notifications.Writer.TryWrite(string.Empty);
        }
    }

    public IReadOnlyList<string> Drain(bool replay = false)
    {
        lock (_gate)
        {
            var batch = messageTypes.Where(type => (replay || _dirty.Contains(type)) && _latest.ContainsKey(type))
                .Select(type => _latest[type]).ToArray();
            _dirty.Clear();
            return batch;
        }
    }

    public void Complete() => _notifications.Writer.TryComplete();
}
