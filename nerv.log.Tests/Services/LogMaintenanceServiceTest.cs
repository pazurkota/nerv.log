using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;

namespace nerv.log.Tests.Services;

public class LogMaintenanceServiceTest
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private LogMaintenanceService CreateService()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_options));
        return new LogMaintenanceService(factory.Object);
    }

    private async Task SeedAsync(params LogEntry[] entries)
    {
        await using var db = new AppDbContext(_options);
        db.Logs.AddRange(entries);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.Logs.CountAsync();
    }

    private static LogEntry Entry(string service, string level, DateTime timestamp) => new()
    {
        TimeStamp = timestamp,
        Level = level,
        ServiceName = service,
        Message = "M"
    };

    private static VacuumRequest Request(int olderThanDays, bool keepErrors = false) => new()
    {
        OlderThanDays = olderThanDays,
        KeepErrors = keepErrors
    };

    private static ServerCallContext Context() => new Mock<ServerCallContext>().Object;

    [Fact]
    public async Task Vacuum_WithEmptyDatabase_DeletesNothing()
    {
        var service = CreateService();

        var response = await service.Vacuum(Request(7), Context());

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task Vacuum_DeletesOnlyLogsOlderThanThreshold()
    {
        await SeedAsync(
            Entry("A", "Info", Now.AddDays(-10)),
            Entry("A", "Info", Now.AddDays(-5)),
            Entry("A", "Info", Now));
        var service = CreateService();

        var response = await service.Vacuum(Request(7), Context());

        Assert.Equal(1, response.DeletedCount);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Vacuum_WithKeepErrors_PreservesErrorAndCriticalLogs()
    {
        await SeedAsync(
            Entry("A", "Info", Now.AddDays(-10)),
            Entry("A", "Error", Now.AddDays(-10)),
            Entry("A", "Critical", Now.AddDays(-10)));
        var service = CreateService();

        var response = await service.Vacuum(Request(7, keepErrors: true), Context());

        Assert.Equal(1, response.DeletedCount);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Vacuum_WithoutKeepErrors_DeletesOldErrorsToo()
    {
        await SeedAsync(
            Entry("A", "Error", Now.AddDays(-10)),
            Entry("A", "Critical", Now.AddDays(-10)));
        var service = CreateService();

        var response = await service.Vacuum(Request(7), Context());

        Assert.Equal(2, response.DeletedCount);
        Assert.Equal(0, await CountAsync());
    }
}
