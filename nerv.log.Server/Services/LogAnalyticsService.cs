using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;

namespace nerv.log.Services;

public class LogAnalyticsService(IDbContextFactory<AppDbContext> dbFactory) : LogAnalytics.LogAnalyticsBase
{
    private static readonly string[] ErrorLevels = ["Error", "Critical"];

    private const int DefaultThreshold = 10;
    private const int DefaultBurstSeconds = 60;
    private const int DefaultBucketSeconds = 300;
    private const double SpikeMultiplier = 3.0;

    public override async Task<StatsResponse> GetStats(StatsRequest request, ServerCallContext context)
    {
        await using var db = await dbFactory.CreateDbContextAsync(context.CancellationToken);

        var from = request.From?.ToDateTime() ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var to = request.To?.ToDateTime() ?? DateTime.UtcNow;

        var query = db.Logs.AsNoTracking()
            .Where(l => l.TimeStamp >= from && l.TimeStamp <= to);

        if (!string.IsNullOrEmpty(request.ServiceName))
        {
            query = query.Where(l => l.ServiceName == request.ServiceName);
        }

        var levels = await query
            .GroupBy(l => l.Level)
            .Select(g => new LevelCount { Level = g.Key, Count = g.LongCount() })
            .ToListAsync(context.CancellationToken);

        var services = await query
            .GroupBy(l => l.ServiceName)
            .Select(g => new ServiceStats
            {
                ServiceName = g.Key,
                Total = g.LongCount(),
                Errors = g.LongCount(l => ErrorLevels.Contains(l.Level))
            })
            .OrderByDescending(s => s.Total)
            .ToListAsync(context.CancellationToken);

        var response = new StatsResponse { TotalLogs = levels.Sum(l => l.Count) };
        response.Levels.AddRange(levels);
        response.Services.AddRange(services);
        return response;
    }

    public override async Task<BruteForceResponse> GetBruteForceFindings
        (BruteForceRequest request, ServerCallContext context)
    {
        await using var db = await dbFactory.CreateDbContextAsync(context.CancellationToken);

        var from = request.From?.ToDateTime() ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var to = request.To?.ToDateTime() ?? DateTime.UtcNow;
        var threshold = request.Threshold > 0 ? request.Threshold : DefaultThreshold;
        var burstWindow = TimeSpan.FromSeconds(request.BurstSeconds > 0 ? request.BurstSeconds : DefaultBurstSeconds);

        var query = db.Logs.AsNoTracking()
            .Where(l => l.TimeStamp >= from && l.TimeStamp <= to && 
                        ((IEnumerable<string>)ErrorLevels).Contains(l.Level));

        if (!string.IsNullOrEmpty(request.ServiceName))
        {
            query = query.Where(l => l.ServiceName == request.ServiceName);
        }

        var failures = await query
            .OrderBy(l => l.TimeStamp)
            .Select(l => new { l.ServiceName, l.TimeStamp })
            .ToListAsync(context.CancellationToken);

        var response = new BruteForceResponse();

        foreach (var group in failures.GroupBy(f => f.ServiceName))
        {
            var timestamps = group.Select(f => f.TimeStamp).ToList();
            var burst = FindDensestBurst(timestamps, burstWindow);

            if (burst.Count >= threshold)
            {
                response.Findings.Add(new BruteForceFinding
                {
                    ServiceName = group.Key,
                    FailureCount = burst.Count,
                    WindowStart = Timestamp.FromDateTime(burst.Start),
                    WindowEnd = Timestamp.FromDateTime(burst.End)
                });
            }
        }

        return response;
    }

    public override async Task<ErrorSpikeResponse> GetErrorSpikes
        (ErrorSpikeRequest request, ServerCallContext context)
    {
        await using var db = await dbFactory.CreateDbContextAsync(context.CancellationToken);

        var from = request.From?.ToDateTime() ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var to = request.To?.ToDateTime() ?? DateTime.UtcNow;
        var threshold = request.Threshold > 0 ? request.Threshold : DefaultThreshold;
        var bucketSize = TimeSpan.FromSeconds(
            request.BucketSeconds > 0 ? request.BucketSeconds : DefaultBucketSeconds);

        var query = db.Logs.AsNoTracking()
            .Where(l => l.TimeStamp >= from && l.TimeStamp <= to &&
                        ((IEnumerable<string>)ErrorLevels).Contains(l.Level));

        if (!string.IsNullOrEmpty(request.ServiceName))
        {
            query = query.Where(l => l.ServiceName == request.ServiceName);
        }

        var errors = await query
            .Select(l => new { l.ServiceName, l.TimeStamp })
            .ToListAsync(context.CancellationToken);

        var response = new ErrorSpikeResponse();
        var bucketCount = Math.Max(1, (int)Math.Ceiling((to - from) / bucketSize));

        foreach (var group in errors.GroupBy(e => e.ServiceName))
        {
            var buckets = new long[bucketCount];
            foreach (var error in group)
            {
                var index = Math.Clamp((int)((error.TimeStamp - from) / bucketSize), 0, bucketCount - 1);
                buckets[index]++;
            }

            var peakIndex = 0;
            for (var i = 1; i < bucketCount; i++)
            {
                if (buckets[i] > buckets[peakIndex])
                {
                    peakIndex = i;
                }
            }

            var peakCount = buckets[peakIndex];
            var baseline = bucketCount > 1
                ? (double)(buckets.Sum() - peakCount) / (bucketCount - 1)
                : 0.0;

            var isSpike = peakCount >= threshold && (baseline == 0.0 || peakCount >= baseline * SpikeMultiplier);

            if (isSpike)
            {
                var bucketStart = from + bucketSize * peakIndex;

                response.Findings.Add(new ErrorSpikeFinding
                {
                    ServiceName = group.Key,
                    ErrorCount = peakCount,
                    BaselineAverage = baseline,
                    BucketStart = Timestamp.FromDateTime(bucketStart),
                    BucketEnd = Timestamp.FromDateTime(bucketStart + bucketSize < to ? bucketStart + bucketSize : to)
                });
            }
        }

        return response;
    }

    private static (int Count, DateTime Start, DateTime End) FindDensestBurst
        (List<DateTime> timestamps, TimeSpan window)
    {
        var best = (Count: 0, Start: DateTime.MinValue, End: DateTime.MinValue);

        var left = 0;
        for (var right = 0; right < timestamps.Count; right++)
        {
            while (timestamps[right] - timestamps[left] > window)
            {
                left++;
            }

            var count = right - left + 1;
            if (count > best.Count)
            {
                best = (count, timestamps[left], timestamps[right]);
            }
        }

        return best;
    }
}
