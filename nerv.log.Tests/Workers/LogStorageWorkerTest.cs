using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;
using nerv.log.Workers;

namespace nerv.log.Tests.Workers;

public class LogStorageWorkerTest
{
    private readonly Mock<ILogger<LogStorageWorker>> _mockLogger;
    private readonly Mock<IDbContextFactory<AppDbContext>> _mockContextFactory;

    public LogStorageWorkerTest()
    {
        _mockLogger = new Mock<ILogger<LogStorageWorker>>();
        _mockContextFactory = new Mock<IDbContextFactory<AppDbContext>>();
    }

    private AppDbContext CreateInMemoryContext(string? dbName = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options);

    private LogStorageWorker CreateWorker(Channel<LogEntry> channel) =>
        new(channel, _mockContextFactory.Object, _mockLogger.Object, new EnvService());

    private static IEnumerable<LogEntry> CreateLogs(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new LogEntry { Message = $"Message {i}", ServiceName = "TestService", Level = "Info" });

    private static async Task WriteAndComplete(Channel<LogEntry> channel, IEnumerable<LogEntry> logs)
    {
        foreach (var log in logs)
            await channel.Writer.WriteAsync(log);
        channel.Writer.Complete();
    }

    // Polls condition every 50 ms until it returns true or the timeout expires.
    // ExecuteAsync runs an orchestration loop that never exits on its own, so tests
    // cannot await ExecuteTask — they must poll for the expected side effect, then
    // call StopAsync to shut down cleanly.
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
    public async Task ExecuteAsync_WithSmallBatch_SavesAllLogsToDatabase()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(5));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var ctx = CreateInMemoryContext(dbName);
            return await ctx.Logs.CountAsync() == 5;
        });
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await using var finalCtx = CreateInMemoryContext(dbName);
        Assert.Equal(5, await finalCtx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyChannel_NeverFlushesToDatabase()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<LogEntry>();
        channel.Writer.Complete();
        var worker = CreateWorker(channel);

        // Act — channel is already empty; give workers time to observe it and exit
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Task.FromResult(channel.Reader.Completion.IsCompleted && channel.Reader.Count == 0));
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchSizeExceeded_SavesAllLogsToDatabase()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(1500));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var ctx = CreateInMemoryContext(dbName);
            return await ctx.Logs.CountAsync() == 1500;
        });
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await using var finalCtx = CreateInMemoryContext(dbName);
        Assert.Equal(1500, await finalCtx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithExactlyBatchSize_SavesAllLogsToDatabase()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(1000));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var ctx = CreateInMemoryContext(dbName);
            return await ctx.Logs.CountAsync() == 1000;
        });
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await using var finalCtx = CreateInMemoryContext(dbName);
        Assert.Equal(1000, await finalCtx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeDataset_SavesAllLogsToDatabase()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(3000));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var ctx = CreateInMemoryContext(dbName);
            return await ctx.Logs.CountAsync() == 3000;
        });
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await using var finalCtx = CreateInMemoryContext(dbName);
        Assert.Equal(3000, await finalCtx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_LogsWarningAndExitsGracefully()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<LogEntry>();
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Worker threads are started via Task.Run (fire-and-forget), so StopAsync may return
        // before they finish logging the cancellation warning — wait for the log to appear.
        await WaitUntilAsync(() => Task.FromResult(
            _mockLogger.Invocations.Any(i =>
                i.Arguments.OfType<Microsoft.Extensions.Logging.LogLevel>()
                    .Any(l => l == Microsoft.Extensions.Logging.LogLevel.Warning)
                && i.Arguments[2]?.ToString()?.Contains("has been stopped") == true)),
            TimeSpan.FromSeconds(5));

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("has been stopped")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbThrows_LogsErrorAndContinues()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(3));
        var worker = CreateWorker(channel);

        // Act — wait until channel is drained and error handling finishes
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Task.FromResult(channel.Reader.Count == 0));
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFlushing_LogsInformationMessages()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext());

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(3));
        var worker = CreateWorker(channel);

        // Act — wait until workers have flushed and logged success
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Task.FromResult(
            _mockLogger.Invocations.Any(i =>
                i.Arguments.OfType<Microsoft.Extensions.Logging.LogLevel>()
                    .Any(l => l == Microsoft.Extensions.Logging.LogLevel.Information)
                && i.Arguments[2]?.ToString()?.Contains("Package successfully saved") == true)));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Saving package with")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Package successfully saved")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbThrows_BatchIsClearedAndWorkerCompletesGracefully()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(1500));
        var worker = CreateWorker(channel);

        // Act — wait for channel to drain (workers read, attempted flushes, caught errors)
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Task.FromResult(channel.Reader.Count == 0));
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Assert — errors were logged; worker finished without deadlock
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
