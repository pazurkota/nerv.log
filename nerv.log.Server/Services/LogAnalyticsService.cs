using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;

namespace nerv.log.Services;

public class LogAnalyticsService(IDbContextFactory<AppDbContext> dbFactory) : LogAnalytics.LogAnalyticsBase
{
    private static readonly string[] ErrorLevels = ["Error", "Critical"];

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
}
