using Grpc.Core;

namespace nerv.log.Services;

public class LogTailService(LogBroadcaster broadcaster) : LogTail.LogTailBase
{
    public override async Task Tail
        (TailRequest request, IServerStreamWriter<LogRequest> responseStream, ServerCallContext context)
    {
        var reader = broadcaster.Subscribe(out var unsubscribe);

        try
        {
            while (await reader.WaitToReadAsync(context.CancellationToken))
            {
                while (reader.TryRead(out var entry))
                {
                    if (Matches(entry, request))
                    {
                        await responseStream.WriteAsync(entry, context.CancellationToken);
                    }
                }
            }
        }
        finally
        {
            unsubscribe();
        }
    }

    private static bool Matches(LogRequest entry, TailRequest request)
    {
        if (!string.IsNullOrEmpty(request.ServiceName) && entry.ServiceName != request.ServiceName)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(request.Environment) && entry.Environment != request.Environment)
        {
            return false;
        }

        if (request.Level != LogLevel.Unspecified && entry.Level != request.Level)
        {
            return false;
        }

        return true;
    }
}
