using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;
using nerv.log.Model;

namespace nerv.log.Workers;

public class LogStorageWorker
        (Channel<LogEntry> channel, 
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<LogStorageWorker> logger) : BackgroundService
{
    private const int BatchSize = 1000;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = channel.Reader;
        var batch = new List<LogEntry>();

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var log))
                {
                    batch.Add(log);

                    if (batch.Count >= BatchSize)
                    {
                        await FlushBatchToDatabaseAsync(batch);
                    }
                }

                if (batch.Any())
                {
                    await FlushBatchToDatabaseAsync(batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("LogStorageWorker is stopping due to cancellation.");
        }
    }

    private async Task FlushBatchToDatabaseAsync(List<LogEntry> batch)
    {
        logger.LogInformation("worker: Saving mass package with {Count} logs to database...", batch.Count);

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            await context.Logs.AddRangeAsync(batch);
            await context.SaveChangesAsync();

            logger.LogInformation("worker: Package successfully saved.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Critical error occured while trying to save package to database.");
        }
        finally
        {
            batch.Clear();
        }
    }
}