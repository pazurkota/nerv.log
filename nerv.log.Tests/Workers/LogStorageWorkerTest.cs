using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using nerv.log.Database;
using nerv.log.Model;
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
        new(channel, _mockContextFactory.Object, _mockLogger.Object);

    private static IEnumerable<LogEntry> CreateLogs(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new LogEntry { Message = $"Message {i}", ServiceName = "TestService", Level = "Info" });

    private static async Task WriteAndComplete(Channel<LogEntry> channel, IEnumerable<LogEntry> logs)
    {
        foreach (var log in logs)
            await channel.Writer.WriteAsync(log);
        channel.Writer.Complete();
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
        await worker.ExecuteTask!;

        // Assert — all 5 logs must reach the DB regardless of worker distribution
        await using var ctx = CreateInMemoryContext(dbName);
        Assert.Equal(5, await ctx.Logs.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyChannel_NeverFlushesToDatabase()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<LogEntry>();
        channel.Writer.Complete();
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchSizeExceeded_SavesAllLogsToDatabase()
    {
        // Arrange – 1500 entries triggers at least one mid-batch flush in addition to the end-of-channel flush
        var dbName = Guid.NewGuid().ToString();
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateInMemoryContext(dbName));

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(1500));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        await using var ctx = CreateInMemoryContext(dbName);
        Assert.Equal(1500, await ctx.Logs.CountAsync());
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
        await worker.ExecuteTask!;

        // Assert
        await using var ctx = CreateInMemoryContext(dbName);
        Assert.Equal(1000, await ctx.Logs.CountAsync());
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
        await worker.ExecuteTask!;

        // Assert
        await using var ctx = CreateInMemoryContext(dbName);
        Assert.Equal(3000, await ctx.Logs.CountAsync());
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

        // Assert — each of the 4 workers logs a warning when stopped
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

        // Act — should not propagate the exception
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert — at least one worker logged the error
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

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert — at least one flush produces both a "Saving" and a "saved" log
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
        // Arrange — DB always fails; verify the worker doesn't hang and errors are reported
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var channel = Channel.CreateUnbounded<LogEntry>();
        // 1500 entries to ensure mid-batch flushes are attempted across workers
        await WriteAndComplete(channel, CreateLogs(1500));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert — errors were logged; worker finished without deadlock or unhandled exception
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
