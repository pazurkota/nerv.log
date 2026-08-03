using System.Collections.Concurrent;
using System.Threading.Channels;

namespace nerv.log.Services;

public class LogBroadcaster
{
    private readonly ConcurrentDictionary<Channel<LogRequest>, byte> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public ChannelReader<LogRequest> Subscribe(out Action unsubscribe)
    {
        var channel = Channel.CreateUnbounded<LogRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _subscribers[channel] = 0;
        unsubscribe = () => _subscribers.TryRemove(channel, out _);

        return channel.Reader;
    }

    public void Publish(LogRequest entry)
    {
        foreach (var channel in _subscribers.Keys)
        {
            channel.Writer.TryWrite(entry);
        }
    }
}
