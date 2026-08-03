using nerv.log.Services;

namespace nerv.log.Tests.Services;

public class LogBroadcasterTest
{
    private static LogRequest MakeLog(string message = "M") => new() { ServiceName = "S", Message = message };

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var broadcaster = new LogBroadcaster();

        var exception = Record.Exception(() => broadcaster.Publish(MakeLog()));

        Assert.Null(exception);
    }

    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        var broadcaster = new LogBroadcaster();
        var reader = broadcaster.Subscribe(out _);

        broadcaster.Publish(MakeLog("hello"));

        Assert.True(reader.TryRead(out var entry));
        Assert.Equal("hello", entry.Message);
    }

    [Fact]
    public void Publish_DeliversToAllSubscribers()
    {
        var broadcaster = new LogBroadcaster();
        var readerA = broadcaster.Subscribe(out _);
        var readerB = broadcaster.Subscribe(out _);

        broadcaster.Publish(MakeLog("hello"));

        Assert.True(readerA.TryRead(out _));
        Assert.True(readerB.TryRead(out _));
    }

    [Fact]
    public void Publish_AfterUnsubscribe_DoesNotDeliverToThatSubscriber()
    {
        var broadcaster = new LogBroadcaster();
        var reader = broadcaster.Subscribe(out var unsubscribe);
        unsubscribe();

        broadcaster.Publish(MakeLog());

        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void Subscribe_IncrementsSubscriberCount()
    {
        var broadcaster = new LogBroadcaster();

        broadcaster.Subscribe(out _);
        broadcaster.Subscribe(out _);

        Assert.Equal(2, broadcaster.SubscriberCount);
    }

    [Fact]
    public void Unsubscribe_DecrementsSubscriberCount()
    {
        var broadcaster = new LogBroadcaster();
        broadcaster.Subscribe(out var unsubscribe);

        unsubscribe();

        Assert.Equal(0, broadcaster.SubscriberCount);
    }
}
