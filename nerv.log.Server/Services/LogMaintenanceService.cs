using System.Diagnostics;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;

namespace nerv.log.Services;

public class LogMaintenanceService(IDbContextFactory<AppDbContext> dbFactory) : LogMaintenance.LogMaintenanceBase
{
    private const string TableName = "Logs";

    public override async Task<VacuumResponse> Vacuum(VacuumRequest request, ServerCallContext context)
    {
        await using var db = await dbFactory.CreateDbContextAsync(context.CancellationToken);

        var sizeBefore = await GetTableSizeAsync(db, context.CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var sql = request.Full ? $"VACUUM (FULL, ANALYZE) \"{TableName}\";" : $"VACUUM (ANALYZE) \"{TableName}\";";
        await db.Database.ExecuteSqlRawAsync(sql, context.CancellationToken);
        stopwatch.Stop();

        var sizeAfter = await GetTableSizeAsync(db, context.CancellationToken);

        return new VacuumResponse
        {
            SizeBeforeBytes = sizeBefore,
            SizeAfterBytes = sizeAfter,
            DurationSeconds = stopwatch.Elapsed.TotalSeconds
        };
    }

    private static async Task<long> GetTableSizeAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await db.Database
            .SqlQueryRaw<long>($"SELECT pg_total_relation_size('\"{TableName}\"') AS \"Value\"")
            .SingleAsync(cancellationToken);
}
