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
