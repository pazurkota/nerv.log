using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;

namespace nerv.log.Workers;

public class LogStorageWorker
        (Channel<LogEntry> channel, 
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<LogStorageWorker> logger,
        EnvService envService) : BackgroundService
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeWorkers = new();
    private int _workerIdCounter = 0;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("worker: Worker scale orchestration available. Min: {min}, Max, {max}",
            envService.MinWorkers, envService.MaxWorkers);

        for (int i = 0; i < envService.MinWorkers; i++)
        {
            ScaleUp(cancellationToken: stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);

            int currentQueueSize = channel.Reader.Count;
            int currentWorkerCount = _activeWorkers.Count;

            if (currentQueueSize > currentWorkerCount * envService.ThresholdByWorker
                && currentWorkerCount < envService.MaxWorkers)
            {
                ScaleUp(cancellationToken: stoppingToken);
            } 
            else if (currentQueueSize < (currentWorkerCount - 1) * envService.ThresholdByWorker
                     && currentWorkerCount > envService.MinWorkers)
            {
                ScaleDown();
            }
        }

        foreach (var workersValue in _activeWorkers.Values) await workersValue.CancelAsync();
    }

    private void ScaleUp(CancellationToken cancellationToken)
    {
        int id = Interlocked.Increment(ref _workerIdCounter);
        var workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_activeWorkers.TryAdd(id, workerCts))
        {
            logger.LogWarning("worker: scaling up: Running worker #{id}", id);
            Task.Run(() => StartWorkerAsync(id, workerCts.Token), workerCts.Token);
        }
    }

    private void ScaleDown()
    {
        var firstWorker = _activeWorkers.Keys.FirstOrDefault(id => id > envService.MinWorkers);

        if (firstWorker == 0 || !_activeWorkers.TryRemove(firstWorker, out var workerCts)) return;
        
        logger.LogWarning("worker: scaling down: Closing worker #{id}", firstWorker);
        workerCts.Cancel();
        workerCts.Dispose();
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

                    if (batch.Count >= envService.BatchSize)
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