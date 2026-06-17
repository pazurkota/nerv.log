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
    private const int WorkersCount = 4; // temp only
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("worker: Multi-thread saving is available. Workers count: {count}", WorkersCount);

        var workers = new Task[WorkersCount];

        for (int i = 0; i < WorkersCount; i++)
        {
            int workerId = i + 1;
            workers[i] = Task.Run(() => StartWorkerAsync(workerId, cancellationToken: stoppingToken), stoppingToken);
        }

        await Task.WhenAll(workers);
        logger.LogInformation("worker: All save threads has been stopped");
    }

    private async Task StartWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        logger.LogInformation("worker #{id}: ready to work.", workerId);

        var reader = channel.Reader;
        var batch = new List<LogEntry>();

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                while (reader.TryRead(out var log))
                {
                    batch.Add(log);

                    if (batch.Count >= BatchSize)
                    {
                        await FlushBatchToDatabaseAsync(workerId, batch);
                    }
                }

                if (batch.Any())
                {
                    await FlushBatchToDatabaseAsync(workerId, batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("worker #{id}: has been stopped.", workerId);
        }
    }

    private async Task FlushBatchToDatabaseAsync(int workerId, List<LogEntry> batch)
    {
        logger.LogInformation("worker {id}: Saving package with {Count} logs to database...", workerId, batch.Count);

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            await context.Logs.AddRangeAsync(batch);
            await context.SaveChangesAsync();

            logger.LogInformation("worker {id}: Package successfully saved.", workerId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "worker #{id}: Error occured while saving to database.", workerId);
        }
        finally
        {
            batch.Clear();
        }
    }
}