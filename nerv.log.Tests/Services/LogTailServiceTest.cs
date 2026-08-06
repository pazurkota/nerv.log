using Grpc.Core;
using Moq.Protected;
using nerv.log.Services;

namespace nerv.log.Tests.Services;

public class LogTailServiceTest
{
    private static Mock<ServerCallContext> CreateMockContext(CancellationToken cancellationToken)
    {
        var mock = new Mock<ServerCallContext>();
        mock.Protected()
            .Setup<CancellationToken>("CancellationTokenCore")
            .Returns(cancellationToken);
        return mock;
    }

    private static LogRequest MakeLog
        (string service = "S", string environment = "", LogLevel level = LogLevel.Info, string message = "M") => new()
    {
        ServiceName = service,
        Environment = environment,
        Level = level,
        Message = message
    };

    private static (Mock<IServerStreamWriter<LogRequest>> Mock, List<LogRequest> Written) CreateStreamWriter()
    {
        var written = new List<LogRequest>();
        var mock = new Mock<IServerStreamWriter<LogRequest>>();
        mock.Setup(w => w.WriteAsync(It.IsAny<LogRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LogRequest, CancellationToken>((entry, _) => written.Add(entry))
            .Returns(Task.CompletedTask);
        return (mock, written);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition was not met within the allowed timeout.");
    }

    [Fact]
    public async Task Tail_WithNoFilter_StreamsAllPublishedLogs()
    {
        var broadcaster = new LogBroadcaster();
        var service = new LogTailService(broadcaster);
        var cts = new CancellationTokenSource();
        var (writer, written) = CreateStreamWriter();

        var tailTask = service.Tail(new TailRequest(), writer.Object, CreateMockContext(cts.Token).Object);

        broadcaster.Publish(MakeLog(message: "hello"));
        await WaitUntilAsync(() => written.Count > 0);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tailTask);

        Assert.Equal("hello", Assert.Single(written).Message);
    }

    [Fact]
    public async Task Tail_FiltersByServiceName()
    {
        var broadcaster = new LogBroadcaster();
        var service = new LogTailService(broadcaster);
        var cts = new CancellationTokenSource();
        var (writer, written) = CreateStreamWriter();

        var request = new TailRequest { ServiceName = "Auth.API" };
        var tailTask = service.Tail(request, writer.Object, CreateMockContext(cts.Token).Object);

        broadcaster.Publish(MakeLog("Payment.Gateway", message: "ignored"));
        broadcaster.Publish(MakeLog("Auth.API", message: "matched"));
        await WaitUntilAsync(() => written.Count > 0);
        await Task.Delay(50);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tailTask);

        Assert.Equal("matched", Assert.Single(written).Message);
    }

    [Fact]
    public async Task Tail_FiltersByEnvironment()
    {
        var broadcaster = new LogBroadcaster();
        var service = new LogTailService(broadcaster);
        var cts = new CancellationTokenSource();
        var (writer, written) = CreateStreamWriter();

        var request = new TailRequest { Environment = "production" };
        var tailTask = service.Tail(request, writer.Object, CreateMockContext(cts.Token).Object);

        broadcaster.Publish(MakeLog(environment: "staging", message: "ignored"));
        broadcaster.Publish(MakeLog(environment: "production", message: "matched"));
        await WaitUntilAsync(() => written.Count > 0);
        await Task.Delay(50);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tailTask);

        Assert.Equal("matched", Assert.Single(written).Message);
    }

    [Fact]
    public async Task Tail_FiltersByLevel()
    {
        var broadcaster = new LogBroadcaster();
        var service = new LogTailService(broadcaster);
        var cts = new CancellationTokenSource();
        var (writer, written) = CreateStreamWriter();

        var request = new TailRequest { Level = LogLevel.Error };
        var tailTask = service.Tail(request, writer.Object, CreateMockContext(cts.Token).Object);

        broadcaster.Publish(MakeLog(level: LogLevel.Info, message: "ignored"));
        broadcaster.Publish(MakeLog(level: LogLevel.Error, message: "matched"));
        await WaitUntilAsync(() => written.Count > 0);
        await Task.Delay(50);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tailTask);

        Assert.Equal("matched", Assert.Single(written).Message);
    }

    [Fact]
    public async Task Tail_UnsubscribesFromBroadcasterWhenCancelled()
    {
        var broadcaster = new LogBroadcaster();
        var service = new LogTailService(broadcaster);
        var cts = new CancellationTokenSource();
        var (writer, _) = CreateStreamWriter();

        var tailTask = service.Tail(new TailRequest(), writer.Object, CreateMockContext(cts.Token).Object);

        Assert.Equal(1, broadcaster.SubscriberCount);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tailTask);

        Assert.Equal(0, broadcaster.SubscriberCount);
    }
}
