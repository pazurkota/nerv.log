using System.Text;
using System.Text.Json;
using Grpc.Core;
using nerv.log.Model;
using RabbitMQ.Client;

namespace nerv.log.Services;

public class LogIngestionService
    (IConnection rabbitConnection, ILogger<LogIngestionService> logger) : LogIngestion.LogIngestionBase
{
    private const string QueueName = "raw-logs";
    
    public override async Task<IngestionResponse> StreamLogs
        (IAsyncStreamReader<LogRequest> requestStream, ServerCallContext context)
    {
        long processedCount = 0;

        await using var channel =
            await rabbitConnection.CreateChannelAsync(cancellationToken: context.CancellationToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: context.CancellationToken);

        var properties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent};

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var grpcRequest = requestStream.Current;

            var dbEntry = new LogEntry
            {
                TimeStamp = grpcRequest.Timestamp?.ToDateTime() ?? DateTime.UtcNow,
                Level = grpcRequest.Level.ToString(),
                ServiceName = grpcRequest.ServiceName,
                Message = grpcRequest.Message
            };

            var payload = JsonSerializer.Serialize(dbEntry);
            var body = Encoding.UTF8.GetBytes(payload);

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: QueueName,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: context.CancellationToken);

            processedCount++;
        }
        
        logger.LogInformation("Stream closed. Received and send to RabbitMQ {Count} logs.", processedCount);

        return new IngestionResponse
        {
            Success = true,
            LogsProcessed = processedCount
        };
    }
}