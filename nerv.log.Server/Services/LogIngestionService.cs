using System.Threading.Channels;
using Grpc.Core;
using nerv.log.Model;

namespace nerv.log.Services;

public class LogIngestionService
    (Channel<LogEntry> channel, ILogger<LogIngestionService> logger) : LogIngestion.LogIngestionBase
{
    public override async Task<IngestionResponse> StreamLogs
        (IAsyncStreamReader<LogRequest> requestStream, ServerCallContext context)
    {
        long processedCount = 0;

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

            if (!channel.Writer.TryWrite(dbEntry))
            {
                throw new RpcException(new Status(StatusCode.ResourceExhausted, "Server buffer overloaded"));
            }

            processedCount++;
        }
        
        logger.LogInformation("Stream closed. Received and send to queue {Count} logs.", processedCount);

        return new IngestionResponse
        {
            Success = true,
            LogsProcessed = processedCount
        };
    }
}