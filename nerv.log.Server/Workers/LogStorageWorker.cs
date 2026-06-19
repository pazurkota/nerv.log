using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;
using RabbitMQ.Client.Events;

namespace nerv.log.Workers;

public class LogStorageWorker
        (IConnection rabbitConnection, 
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<LogStorageWorker> logger,
        EnvService envService) : BackgroundService
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeWorkers = new();
    private int _workerIdCounter = 0;
    private const string QueueName = "raw-logs";
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("worker: Worker scale orchestration via RabbitMQ available. Min: {min}, Max, {max}",
            envService.MinWorkers, envService.MaxWorkers);

        for (int i = 0; i < envService.MinWorkers; i++)
        {
            ScaleUp(cancellationToken: stoppingToken);
        }

        await using var controlChannel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);
        

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);

            var queueDeclareResult = await controlChannel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: stoppingToken);

            uint currentQueueSize = queueDeclareResult.MessageCount;
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
        logger.LogInformation("worker #{id}: ready to consume from RabbitMQ.", workerId);

        IChannel? workerChannel = null;
        var batch = new List<LogEntry>();

        try
        {
            workerChannel = await rabbitConnection.CreateChannelAsync(cancellationToken: cancellationToken);
            await workerChannel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var deliveryTags = new List<ulong>();

            var consumer = new AsyncEventingBasicConsumer(workerChannel);

            consumer.ReceivedAsync += async (Model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                try
                {
                    var logEntry = JsonSerializer.Deserialize<LogEntry>(message);
                    if (logEntry != null)
                    {
                        batch.Add(logEntry);
                        deliveryTags.Add(ea.DeliveryTag);
                    }

                    if (batch.Count >= envService.BatchSize)
                    {
                        await FlushBatchToDatabaseAsync(workerId, batch);

                        var highestTag = deliveryTags.Max();
                        await workerChannel.BasicAckAsync(
                            deliveryTag: highestTag,
                            multiple: true,
                            cancellationToken: cancellationToken);
                        deliveryTags.Clear();
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "worker #{Id}: Failed to process incoming message.", workerId);
                    await workerChannel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: true,
                        cancellationToken: cancellationToken);
                }
            };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("worker #{Id}: Has been stopped", workerId);
        }
        finally
        {
            if (workerChannel != null)
            {
                await workerChannel.DisposeAsync();
            }
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