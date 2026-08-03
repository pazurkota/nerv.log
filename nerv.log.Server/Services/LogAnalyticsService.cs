using System.Text.Json;
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

        if (!string.IsNullOrEmpty(request.Environment))
        {
            query = query.Where(l => l.Environment == request.Environment);
        }

        var rows = await query
            .Select(l => new { l.Level, l.ServiceName, l.Metadata })
            .ToListAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.MetadataKey))
        {
            rows = rows.Where(r => MatchesMetadata(r.Metadata, request.MetadataKey, request.MetadataValue)).ToList();
        }

        var levels = rows
            .GroupBy(r => r.Level)
            .Select(g => new LevelCount { Level = g.Key, Count = g.LongCount() })
            .ToList();

        var services = rows
            .GroupBy(r => r.ServiceName)
            .Select(g => new ServiceStats
            {
                ServiceName = g.Key,
                Total = g.LongCount(),
                Errors = g.LongCount(r => ErrorLevels.Contains(r.Level))
            })
            .OrderByDescending(s => s.Total)
            .ToList();

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

        if (!string.IsNullOrEmpty(request.Environment))
        {
            query = query.Where(l => l.Environment == request.Environment);
        }

        var rows = await query
            .OrderBy(l => l.TimeStamp)
            .Select(l => new { l.ServiceName, l.TimeStamp, l.Metadata })
            .ToListAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.MetadataKey))
        {
            rows = rows.Where(r => MatchesMetadata(r.Metadata, request.MetadataKey, request.MetadataValue)).ToList();
        }

        var failures = rows.Select(r => new { r.ServiceName, r.TimeStamp }).ToList();

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

        if (!string.IsNullOrEmpty(request.Environment))
        {
            query = query.Where(l => l.Environment == request.Environment);
        }

        var rows = await query
            .Select(l => new { l.ServiceName, l.TimeStamp, l.Metadata })
            .ToListAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.MetadataKey))
        {
            rows = rows.Where(r => MatchesMetadata(r.Metadata, request.MetadataKey, request.MetadataValue)).ToList();
        }

        var errors = rows.Select(r => new { r.ServiceName, r.TimeStamp }).ToList();

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

    private static bool MatchesMetadata(string? metadataJson, string key, string value)
    {
        if (string.IsNullOrEmpty(metadataJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(key, out var property))
            {
                return false;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() == value,
                JsonValueKind.Number => property.GetRawText() == value,
                JsonValueKind.True => bool.TryParse(value, out var b) && b,
                JsonValueKind.False => bool.TryParse(value, out var b) && !b,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
