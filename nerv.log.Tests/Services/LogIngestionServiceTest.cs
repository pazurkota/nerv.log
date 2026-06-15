using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using nerv.log.Model;
using nerv.log.Services;
using LogLevel = nerv.log.LogLevel;

namespace nerv.log.Tests.Services;

public class LogIngestionServiceTest
{
    private readonly Channel<LogEntry> _channel;
    private readonly Mock<Microsoft.Extensions.Logging.ILogger<LogIngestionService>> _mockLogger;
    
    public LogIngestionServiceTest()
    {
        _channel = Channel.CreateUnbounded<LogEntry>();
        _mockLogger = new Mock<ILogger<LogIngestionService>>();
    }

    [Fact]
    public async Task StreamLogs_WithValidLogs_ShouldProcessSuccessfully()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = LogLevel.Info,
                ServiceName = "TestService",
                Message = "Test message 1"
            },
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(1)),
                Level = LogLevel.Error,
                ServiceName = "TestService",
                Message = "Test message 2"
            }
        };

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        var response = await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(2, response.LogsProcessed);
        
        // Verify logs were written to channel
        var processedLogs = await ReadAllFromChannel(2);
        Assert.Equal(2, processedLogs.Count);
        Assert.Equal("TestService", processedLogs[0].ServiceName);
        Assert.Equal("Test message 1", processedLogs[0].Message);
        Assert.Equal("Test message 2", processedLogs[1].Message);
    }

    [Fact]
    public async Task StreamLogs_WithNullTimestamp_ShouldUseCurrentUtcTime()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        var beforeTime = DateTime.UtcNow;
        
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = null,
                Level = LogLevel.Debug,
                ServiceName = "TestService",
                Message = "Message without timestamp"
            }
        };

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        var response = await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);
        var afterTime = DateTime.UtcNow;

        // Assert
        Assert.True(response.Success);
        var processedLogs = await ReadAllFromChannel(1);
        
        Assert.Single(processedLogs);
        Assert.True(processedLogs[0].TimeStamp >= beforeTime && processedLogs[0].TimeStamp <= afterTime);
    }

    [Fact]
    public async Task StreamLogs_WithMultipleLogs_ShouldProcessAllAndCountCorrectly()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        const int logCount = 10;
        
        var logs = new List<LogRequest>();
        for (int i = 0; i < logCount; i++)
        {
            logs.Add(new LogRequest
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(i)),
                Level = (LogLevel)(i % 6 + 1),
                ServiceName = $"Service{i}",
                Message = $"Message {i}"
            });
        }

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        var response = await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(logCount, response.LogsProcessed);
        
        var processedLogs = await ReadAllFromChannel(logCount);
        Assert.Equal(logCount, processedLogs.Count);
        
        for (int i = 0; i < logCount; i++)
        {
            Assert.Equal($"Service{i}", processedLogs[i].ServiceName);
            Assert.Equal($"Message {i}", processedLogs[i].Message);
        }
    }

    [Fact]
    public async Task StreamLogs_WithEmptyStream_ShouldReturnSuccessWithZeroCount()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        var emptyLogs = new List<LogRequest>();

        var requestStreamMock = CreateMockStreamReader(emptyLogs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        var response = await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(0, response.LogsProcessed);
    }

    [Fact]
    public async Task StreamLogs_WithChannelBufferFull_ShouldThrowResourceExhaustedException()
    {
        // Arrange
        // Create a bounded channel with small capacity
        var boundedChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Pre-fill the channel
        await boundedChannel.Writer.WriteAsync(new LogEntry { Message = "Full" });

        var service = new LogIngestionService(boundedChannel, _mockLogger.Object);
        
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = LogLevel.Error,
                ServiceName = "TestService",
                Message = "This should fail"
            }
        };

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object));
        
        Assert.Equal(StatusCode.ResourceExhausted, ex.Status.StatusCode);
        Assert.Contains("buffer overloaded", ex.Status.Detail);
    }

    [Fact]
    public async Task StreamLogs_ShouldLogInformationMessage()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = LogLevel.Info,
                ServiceName = "TestService",
                Message = "Test log"
            }
        };

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Stream closed") && v.ToString()!.Contains("1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StreamLogs_ShouldPreserveLogLevels()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        
        var logs = new List<LogRequest>
        {
            new() { Level = LogLevel.Trace, ServiceName = "S1", Message = "M1", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Debug, ServiceName = "S2", Message = "M2", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Info, ServiceName = "S3", Message = "M3", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Warning, ServiceName = "S4", Message = "M4", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Error, ServiceName = "S5", Message = "M5", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Critical, ServiceName = "S6", Message = "M6", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) }
        };

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        var response = await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);

        // Assert
        Assert.Equal(6, response.LogsProcessed);
        var processedLogs = await ReadAllFromChannel(6);
        
        Assert.Equal("Trace", processedLogs[0].Level);
        Assert.Equal("Debug", processedLogs[1].Level);
        Assert.Equal("Info", processedLogs[2].Level);
        Assert.Equal("Warning", processedLogs[3].Level);
        Assert.Equal("Error", processedLogs[4].Level);
        Assert.Equal("Critical", processedLogs[5].Level);
    }

    [Fact]
    public async Task StreamLogs_ShouldHandleMultipleLogsAndCompleteSuccessfully()
    {
        // Arrange
        var service = new LogIngestionService(_channel, _mockLogger.Object);
        
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = LogLevel.Info,
                ServiceName = "Service1",
                Message = "Message 1"
            },
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(1)),
                Level = LogLevel.Warning,
                ServiceName = "Service2",
                Message = "Message 2"
            },
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(2)),
                Level = LogLevel.Error,
                ServiceName = "Service3",
                Message = "Message 3"
            }
        };

        var requestStreamMock = CreateMockStreamReader(logs);
        var serverCallContextMock = new Mock<ServerCallContext>();

        // Act
        var response = await service.StreamLogs(requestStreamMock.Object, serverCallContextMock.Object);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(3, response.LogsProcessed);
        
        var processedLogs = await ReadAllFromChannel(3);
        Assert.Equal(3, processedLogs.Count);
        Assert.Equal("Service1", processedLogs[0].ServiceName);
        Assert.Equal("Service2", processedLogs[1].ServiceName);
        Assert.Equal("Service3", processedLogs[2].ServiceName);
    }

    // Helper methods
    private Mock<IAsyncStreamReader<LogRequest>> CreateMockStreamReader(
        List<LogRequest> logs, 
        CancellationToken cancellationToken = default)
    {
        var enumerator = logs.GetEnumerator();
        var mockStreamReader = new Mock<IAsyncStreamReader<LogRequest>>();

        mockStreamReader
            .Setup(x => x.MoveNext(cancellationToken))
            .Returns(() =>
            {
                var hasNext = enumerator.MoveNext();
                return Task.FromResult(hasNext);
            });

        mockStreamReader
            .Setup(x => x.Current)
            .Returns(() => enumerator.Current);

        return mockStreamReader;
    }

    private async Task<List<LogEntry>> ReadAllFromChannel(int expectedCount)
    {
        var result = new List<LogEntry>();
        var reader = _channel.Reader;

        try
        {
            while (result.Count < expectedCount && reader.TryRead(out var log))
            {
                result.Add(log);
            }

            // If we haven't got all items yet, wait a bit more
            if (result.Count < expectedCount)
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (result.Count < expectedCount)
                {
                    var log = await reader.ReadAsync(cts.Token);
                    result.Add(log);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }

        return result;
    }
}