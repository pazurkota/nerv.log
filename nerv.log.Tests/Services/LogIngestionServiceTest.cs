using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using nerv.log.Model;
using nerv.log.Services;
using RabbitMQ.Client;
using LogLevel = nerv.log.LogLevel;

namespace nerv.log.Tests.Services;

public class LogIngestionServiceTest
{
    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IChannel> _mockRabbitChannel;
    private readonly Mock<ILogger<LogIngestionService>> _mockLogger;
    private readonly LogBroadcaster _broadcaster = new();
    private readonly List<(string Exchange, string RoutingKey, byte[] Body)> _published = [];

    public LogIngestionServiceTest()
    {
        _mockRabbitChannel = new Mock<IChannel>();
        _mockConnection = new Mock<IConnection>();
        _mockLogger = new Mock<ILogger<LogIngestionService>>();

        _mockConnection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockRabbitChannel.Object);

        _mockRabbitChannel
            .Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("raw-logs", 0, 0));

        _mockRabbitChannel
            .Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (exchange, routingKey, _, _, body, _) =>
                    _published.Add((exchange, routingKey, body.ToArray())))
            .Returns(ValueTask.CompletedTask);

        _mockRabbitChannel
            .Setup(c => c.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
    }

    private LogIngestionService CreateService() =>
        new(_mockConnection.Object, _mockLogger.Object, _broadcaster);

    private static Mock<ServerCallContext> CreateMockContext()
    {
        var mock = new Mock<ServerCallContext>();
        return mock;
    }

    private static Mock<IAsyncStreamReader<LogRequest>> CreateStreamReader(IList<LogRequest> logs)
    {
        var index = -1;
        var mock = new Mock<IAsyncStreamReader<LogRequest>>();
        mock.Setup(r => r.MoveNext(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++index < logs.Count);
        mock.Setup(r => r.Current)
            .Returns(() => logs[index]);
        return mock;
    }

    [Fact]
    public async Task StreamLogs_WithValidLogs_ReturnsSuccessAndCorrectCount()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new() { Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info, ServiceName = "TestService", Message = "Message 1" },
            new() { Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Error, ServiceName = "TestService", Message = "Message 2" }
        };

        var response = await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.True(response.Success);
        Assert.Equal(2, response.LogsProcessed);
    }

    [Fact]
    public async Task StreamLogs_WithEmptyStream_ReturnsSuccessWithZeroCount()
    {
        var service = CreateService();

        var response = await service.StreamLogs(CreateStreamReader([]).Object, CreateMockContext().Object);

        Assert.True(response.Success);
        Assert.Equal(0, response.LogsProcessed);
    }

    [Fact]
    public async Task StreamLogs_WithNullTimestamp_UsesCurrentUtcTime()
    {
        var service = CreateService();
        var before = DateTime.UtcNow;

        var logs = new List<LogRequest>
        {
            new() { Timestamp = null, Level = LogLevel.Info, ServiceName = "Service", Message = "No timestamp" }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);
        var after = DateTime.UtcNow;

        Assert.Single(_published);
        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry);
        Assert.True(entry.TimeStamp >= before && entry.TimeStamp <= after);
    }

    [Fact]
    public async Task StreamLogs_DeclaresQueueWithCorrectParameters()
    {
        var service = CreateService();

        await service.StreamLogs(CreateStreamReader([]).Object, CreateMockContext().Object);

        _mockRabbitChannel.Verify(c => c.QueueDeclareAsync(
            "raw-logs",
            true,
            false,
            false,
            null,
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamLogs_PublishesToCorrectExchangeAndQueue()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new() { Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info, ServiceName = "S", Message = "M" }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Single(_published);
        Assert.Equal(string.Empty, _published[0].Exchange);
        Assert.Equal("raw-logs", _published[0].RoutingKey);
    }

    [Fact]
    public async Task StreamLogs_PublishesCorrectJsonPayload()
    {
        var service = CreateService();
        var timestamp = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var logs = new List<LogRequest>
        {
            new() { Timestamp = Timestamp.FromDateTime(timestamp), Level = LogLevel.Error, ServiceName = "MyService", Message = "Error occurred" }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Single(_published);
        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry);
        Assert.Equal("Error", entry.Level);
        Assert.Equal("MyService", entry.ServiceName);
        Assert.Equal("Error occurred", entry.Message);
        Assert.Equal(timestamp, entry.TimeStamp);
    }

    [Fact]
    public async Task StreamLogs_PublishesCorrectEnvironment()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info,
                ServiceName = "MyService", Environment = "production", Message = "Message"
            }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Single(_published);
        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry);
        Assert.Equal("production", entry.Environment);
    }

    [Fact]
    public async Task StreamLogs_WithEmptyEnvironment_DefaultsToEmptyString()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info,
                ServiceName = "MyService", Message = "Message"
            }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Single(_published);
        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry);
        Assert.Equal(string.Empty, entry.Environment);
    }

    [Fact]
    public async Task StreamLogs_WithMetadata_SerializesStructAsJson()
    {
        var service = CreateService();
        var metadata = new Struct();
        metadata.Fields.Add("user_id", Value.ForString("abc123"));
        metadata.Fields.Add("retry_count", Value.ForNumber(3));
        metadata.Fields.Add("is_authenticated", Value.ForBool(true));

        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info,
                ServiceName = "MyService", Message = "Message", Metadata = metadata
            }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Single(_published);
        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry);
        Assert.NotNull(entry.Metadata);

        using var storedJson = JsonDocument.Parse(entry.Metadata);
        Assert.Equal("abc123", storedJson.RootElement.GetProperty("user_id").GetString());
        Assert.Equal(3, storedJson.RootElement.GetProperty("retry_count").GetDouble());
        Assert.True(storedJson.RootElement.GetProperty("is_authenticated").GetBoolean());
    }

    [Fact]
    public async Task StreamLogs_WithNestedMetadata_PreservesStructure()
    {
        var service = CreateService();
        var nested = new Struct();
        nested.Fields.Add("code", Value.ForNumber(500));
        var metadata = new Struct();
        metadata.Fields.Add("error", Value.ForStruct(nested));

        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Error,
                ServiceName = "MyService", Message = "Message", Metadata = metadata
            }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry?.Metadata);

        using var storedJson = JsonDocument.Parse(entry.Metadata);
        Assert.Equal(500, storedJson.RootElement.GetProperty("error").GetProperty("code").GetDouble());
    }

    [Fact]
    public async Task StreamLogs_WithoutMetadata_LeavesMetadataNull()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info,
                ServiceName = "MyService", Message = "Message"
            }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Single(_published);
        var entry = JsonSerializer.Deserialize<LogEntry>(_published[0].Body);
        Assert.NotNull(entry);
        Assert.Null(entry.Metadata);
    }

    [Fact]
    public async Task StreamLogs_WithMultipleLogs_PublishesAllMessages()
    {
        var service = CreateService();
        const int count = 10;

        var logs = Enumerable.Range(0, count)
            .Select(i => new LogRequest
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = LogLevel.Info,
                ServiceName = $"Service{i}",
                Message = $"Message {i}"
            }).ToList();

        var response = await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Equal(count, response.LogsProcessed);
        Assert.Equal(count, _published.Count);

        for (var i = 0; i < count; i++)
        {
            var entry = JsonSerializer.Deserialize<LogEntry>(_published[i].Body);
            Assert.Equal($"Service{i}", entry!.ServiceName);
            Assert.Equal($"Message {i}", entry.Message);
        }
    }

    [Fact]
    public async Task StreamLogs_ShouldPreserveLogLevels()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new() { Level = LogLevel.Trace, ServiceName = "S", Message = "M", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Debug, ServiceName = "S", Message = "M", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Info, ServiceName = "S", Message = "M", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Warning, ServiceName = "S", Message = "M", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Error, ServiceName = "S", Message = "M", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            new() { Level = LogLevel.Critical, ServiceName = "S", Message = "M", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.Equal(6, _published.Count);
        var levels = _published
            .Select(p => JsonSerializer.Deserialize<LogEntry>(p.Body)!.Level)
            .ToList();

        Assert.Equal(["Trace", "Debug", "Info", "Warning", "Error", "Critical"], levels);
    }

    [Fact]
    public async Task StreamLogs_LogsInformationWithProcessedCount()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new() { Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info, ServiceName = "S", Message = "M" }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        _mockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Stream closed") && v.ToString()!.Contains("1")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StreamLogs_PublishesReceivedLogsToBroadcaster()
    {
        var service = CreateService();
        var reader = _broadcaster.Subscribe(out _);

        var logs = new List<LogRequest>
        {
            new()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Error,
                ServiceName = "MyService", Environment = "production", Message = "Boom"
            }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.True(reader.TryRead(out var broadcast));
        Assert.Equal("MyService", broadcast.ServiceName);
        Assert.Equal("production", broadcast.Environment);
        Assert.Equal("Boom", broadcast.Message);
        Assert.Equal(LogLevel.Error, broadcast.Level);
    }

    [Fact]
    public async Task StreamLogs_WithNullTimestamp_BroadcastsCurrentUtcTime()
    {
        var service = CreateService();
        var reader = _broadcaster.Subscribe(out _);
        var before = DateTime.UtcNow;

        var logs = new List<LogRequest>
        {
            new() { Timestamp = null, Level = LogLevel.Info, ServiceName = "S", Message = "M" }
        };

        await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);
        var after = DateTime.UtcNow;

        Assert.True(reader.TryRead(out var broadcast));
        var broadcastTime = broadcast.Timestamp.ToDateTime();
        Assert.True(broadcastTime >= before && broadcastTime <= after);
    }

    [Fact]
    public async Task StreamLogs_WithNoSubscribers_DoesNotThrow()
    {
        var service = CreateService();
        var logs = new List<LogRequest>
        {
            new() { Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Level = LogLevel.Info, ServiceName = "S", Message = "M" }
        };

        var response = await service.StreamLogs(CreateStreamReader(logs).Object, CreateMockContext().Object);

        Assert.True(response.Success);
    }
}
