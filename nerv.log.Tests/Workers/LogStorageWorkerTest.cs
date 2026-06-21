using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;
using nerv.log.Workers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace nerv.log.Tests.Workers;

public class LogStorageWorkerTest : IDisposable
{
    private readonly Mock<ILogger<LogStorageWorker>> _mockLogger;
    private readonly Mock<IDbContextFactory<AppDbContext>> _mockContextFactory;
    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IChannel> _mockChannel;
    private readonly List<AsyncEventingBasicConsumer> _consumers = [];

    public LogStorageWorkerTest()
    {
        Environment.SetEnvironmentVariable("SCALING_MIN_WORKERS", "1");
        Environment.SetEnvironmentVariable("SCALING_MAX_WORKERS", "1");
        Environment.SetEnvironmentVariable("SCALING_BATCH_SIZE", "5");

        _mockLogger = new Mock<ILogger<LogStorageWorker>>();
        _mockContextFactory = new Mock<IDbContextFactory<AppDbContext>>();
        _mockConnection = new Mock<IConnection>();
        _mockChannel = new Mock<IChannel>();

        _mockConnection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);

        _mockChannel
            .Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("raw-logs", 0, 0));

        _mockChannel
            .Setup(c => c.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => _consumers.Add((AsyncEventingBasicConsumer)consumer))
            .ReturnsAsync("consumer-tag");

        _mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        _mockChannel
            .Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        _mockChannel
            .Setup(c => c.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SCALING_MIN_WORKERS", null);
        Environment.SetEnvironmentVariable("SCALING_MAX_WORKERS", null);
        Environment.SetEnvironmentVariable("SCALING_BATCH_SIZE", null);
    }

    private AppDbContext CreateInMemoryContext(string? dbName = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options);

    private LogStorageWorker CreateWorker() =>
        new(_mockConnection.Object, _mockContextFactory.Object, _mockLogger.Object, new EnvService());

    private static ReadOnlyMemory<byte> Serialize(LogEntry entry) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry));

    private static LogEntry MakeLog(string message = "test") =>
        new() { Message = message, ServiceName = "TestService", Level = "Info" };

    private async Task<AsyncEventingBasicConsumer> WaitForConsumerAsync()
    {
        await WaitUntilAsync(() => Task.FromResult(_consumers.Count > 0));
        return _consumers.First();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition was not met within the allowed timeout.");
    }

    [Fact]
    public async Task ExecuteAsync_RegistersConsumerWithCorrectQueue()
    {
        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await WaitForConsumerAsync();
        await worker.StopAsync(CancellationToken.None);

        _mockChannel.Verify(c => c.BasicConsumeAsync(
            "raw-logs",
            false,
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchSizeReached_SavesLogsToDatabase()
    {
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var consumer = await WaitForConsumerAsync();

        for (ulong i = 1; i <= 5; i++)
            await consumer.HandleBasicDeliverAsync("tag", i, false, "", "raw-logs", new BasicProperties(), Serialize(MakeLog($"Message {i}")));

        await WaitUntilAsync(async () =>
        {
            await using var ctx = CreateInMemoryContext(dbName);
            return await ctx.Logs.CountAsync() == 5;
        });
        await worker.StopAsync(CancellationToken.None);

        await using var finalCtx = CreateInMemoryContext(dbName);
        Assert.Equal(5, await finalCtx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchSizeReached_AcknowledgesWithHighestDeliveryTag()
    {
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext());

        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var consumer = await WaitForConsumerAsync();

        for (ulong i = 1; i <= 5; i++)
            await consumer.HandleBasicDeliverAsync("tag", i, false, "", "raw-logs", new BasicProperties(), Serialize(MakeLog()));

        await WaitUntilAsync(() => Task.FromResult(
            _mockChannel.Invocations.Any(inv => inv.Method.Name == nameof(IChannel.BasicAckAsync))));
        await worker.StopAsync(CancellationToken.None);

        _mockChannel.Verify(c => c.BasicAckAsync(5UL, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppedWithPartialBatch_FlushesRemainingLogs()
    {
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var consumer = await WaitForConsumerAsync();

        for (ulong i = 1; i <= 3; i++)
            await consumer.HandleBasicDeliverAsync("tag", i, false, "", "raw-logs", new BasicProperties(), Serialize(MakeLog($"Message {i}")));

        await worker.StopAsync(CancellationToken.None);

        // Cleanup runs on Task.Run thread after StopAsync returns
        await WaitUntilAsync(async () =>
        {
            await using var ctx = CreateInMemoryContext(dbName);
            return await ctx.Logs.CountAsync() == 3;
        }, TimeSpan.FromSeconds(5));

        await using var finalCtx = CreateInMemoryContext(dbName);
        Assert.Equal(3, await finalCtx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppedWithPartialBatch_AcknowledgesRemainingMessages()
    {
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext());

        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var consumer = await WaitForConsumerAsync();

        for (ulong i = 1; i <= 3; i++)
            await consumer.HandleBasicDeliverAsync("tag", i, false, "", "raw-logs", new BasicProperties(), Serialize(MakeLog()));

        await worker.StopAsync(CancellationToken.None);

        await WaitUntilAsync(() => Task.FromResult(
            _mockChannel.Invocations.Any(inv =>
                inv.Method.Name == nameof(IChannel.BasicAckAsync)
                && (ulong)inv.Arguments[0] == 3UL)),
            TimeSpan.FromSeconds(5));

        _mockChannel.Verify(c => c.BasicAckAsync(3UL, true, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbThrows_LogsError()
    {
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var consumer = await WaitForConsumerAsync();

        for (ulong i = 1; i <= 5; i++)
            await consumer.HandleBasicDeliverAsync("tag", i, false, "", "raw-logs", new BasicProperties(), Serialize(MakeLog()));

        await WaitUntilAsync(() => Task.FromResult(
            _mockLogger.Invocations.Any(inv =>
                inv.Arguments.OfType<MsLogLevel>().Any(l => l == MsLogLevel.Error))));
        await worker.StopAsync(CancellationToken.None);

        _mockLogger.Verify(
            x => x.Log(
                MsLogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidJson_NacksMessage()
    {
        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        var consumer = await WaitForConsumerAsync();

        var invalidBody = Encoding.UTF8.GetBytes("{not valid json");
        await consumer.HandleBasicDeliverAsync("tag", 1UL, false, "", "raw-logs", new BasicProperties(), invalidBody);

        await WaitUntilAsync(() => Task.FromResult(
            _mockChannel.Invocations.Any(inv => inv.Method.Name == nameof(IChannel.BasicNackAsync))));
        await worker.StopAsync(CancellationToken.None);

        _mockChannel.Verify(c => c.BasicNackAsync(1UL, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
