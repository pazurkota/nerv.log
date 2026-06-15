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

    private AppDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
    public async Task ExecuteAsync_WithSmallBatch_FlushesOnceWhenChannelCompletes()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInMemoryContext);

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(5));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task ExecuteAsync_WhenBatchSizeReached_FlushesImmediately()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInMemoryContext);

        var channel = Channel.CreateUnbounded<LogEntry>();
        // 1500 entries: flush at 1000, then flush remaining 500 when channel completes
        await WriteAndComplete(channel, CreateLogs(1500));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithExactlyBatchSize_FlushesOnce()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInMemoryContext);

        var channel = Channel.CreateUnbounded<LogEntry>();
        // Exactly 1000 entries: batch flush happens in the inner while, batch is empty after
        await WriteAndComplete(channel, CreateLogs(1000));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultiplesOfBatchSize_FlushesCorrectNumberOfTimes()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInMemoryContext);

        var channel = Channel.CreateUnbounded<LogEntry>();
        // 3000 entries: flush at 1000, 2000, and 3000 (when channel completes)
        await WriteAndComplete(channel, CreateLogs(3000));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
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

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("stopping due to cancellation")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFlushing_LogsStartAndSuccessMessages()
    {
        // Arrange
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInMemoryContext);

        var channel = Channel.CreateUnbounded<LogEntry>();
        await WriteAndComplete(channel, CreateLogs(3));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert – one "Saving..." and one "Package successfully saved." per flush
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Saving mass package")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Package successfully saved")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbThrows_BatchIsClearedBeforeNextFlush()
    {
        // Arrange – first call throws, subsequent calls succeed; both batches are separate
        var callCount = 0;
        _mockContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) throw new InvalidOperationException("First flush fails");
                return CreateInMemoryContext();
            });

        var channel = Channel.CreateUnbounded<LogEntry>();
        // 1500 entries: first flush at 1000 (throws), second flush at 500 remaining (succeeds)
        await WriteAndComplete(channel, CreateLogs(1500));
        var worker = CreateWorker(channel);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert – both flushes were attempted; second one succeeded despite first failing
        _mockContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
