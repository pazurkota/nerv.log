using System.Diagnostics;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;

namespace nerv.log.Services;

public class LogMaintenanceService(IDbContextFactory<AppDbContext> dbFactory) : LogMaintenance.LogMaintenanceBase
{
    private static readonly string[] ErrorLevels = ["Error", "Critical"];

    private const int DefaultOlderThanDays = 7;

    public override async Task<VacuumResponse> Vacuum(VacuumRequest request, ServerCallContext context)
    {
        await using var db = await dbFactory.CreateDbContextAsync(context.CancellationToken);

        var olderThanDays = request.OlderThanDays > 0 ? request.OlderThanDays : DefaultOlderThanDays;
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);

        var query = db.Logs.Where(l => l.TimeStamp < cutoff);

        if (request.KeepErrors)
        {
            query = query.Where(l => !ErrorLevels.Contains(l.Level));
        }

        var stopwatch = Stopwatch.StartNew();
        var toDelete = await query.ToListAsync(context.CancellationToken);
        db.Logs.RemoveRange(toDelete);
        await db.SaveChangesAsync(context.CancellationToken);
        stopwatch.Stop();

        return new VacuumResponse
        {
            DeletedCount = toDelete.Count,
            DurationSeconds = stopwatch.Elapsed.TotalSeconds
        };
    }
}
