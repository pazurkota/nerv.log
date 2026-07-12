using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;

namespace nerv.log.Tests.Services;

public class LogAnalyticsServiceTest
{
    private static readonly DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private LogAnalyticsService CreateService()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_options));
        return new LogAnalyticsService(factory.Object);
    }

    private async Task SeedAsync(params LogEntry[] entries)
    {
        await using var db = new AppDbContext(_options);
        db.Logs.AddRange(entries);
        await db.SaveChangesAsync();
    }

    private static LogEntry Entry(string service, string level, DateTime? timestamp = null) => new()
    {
        TimeStamp = timestamp ?? Now,
        Level = level,
        ServiceName = service,
        Message = "M"
    };

    private static StatsRequest Request(DateTime from, DateTime to, string service = "") => new()
    {
        From = Timestamp.FromDateTime(from),
        To = Timestamp.FromDateTime(to),
        ServiceName = service
    };

    private static ServerCallContext Context() => new Mock<ServerCallContext>().Object;

    [Fact]
    public async Task GetStats_WithEmptyDatabase_ReturnsZeroTotals()
    {
        var service = CreateService();

        var response = await service.GetStats(Request(Now.AddHours(-1), Now), Context());

        Assert.Equal(0, response.TotalLogs);
        Assert.Empty(response.Levels);
        Assert.Empty(response.Services);
    }

    [Fact]
    public async Task GetStats_CountsLogsPerLevel()
    {
        await SeedAsync(
            Entry("A", "Info"),
            Entry("A", "Info"),
            Entry("A", "Error"));
        var service = CreateService();

        var response = await service.GetStats(Request(Now.AddHours(-1), Now), Context());

        Assert.Equal(3, response.TotalLogs);
        Assert.Equal(2, response.Levels.Single(l => l.Level == "Info").Count);
        Assert.Equal(1, response.Levels.Single(l => l.Level == "Error").Count);
    }

    [Fact]
    public async Task GetStats_CountsErrorsPerServiceAndOrdersByTotal()
    {
        await SeedAsync(
            Entry("Busy", "Info"),
            Entry("Busy", "Error"),
            Entry("Busy", "Critical"),
            Entry("Quiet", "Warning"));
        var service = CreateService();

        var response = await service.GetStats(Request(Now.AddHours(-1), Now), Context());

        Assert.Equal(["Busy", "Quiet"], response.Services.Select(s => s.ServiceName));

        var busy = response.Services[0];
        Assert.Equal(3, busy.Total);
        Assert.Equal(2, busy.Errors);

        var quiet = response.Services[1];
        Assert.Equal(1, quiet.Total);
        Assert.Equal(0, quiet.Errors);
    }

    [Fact]
    public async Task GetStats_FiltersByTimeWindow()
    {
        await SeedAsync(
            Entry("A", "Info", Now.AddMinutes(-30)),
            Entry("A", "Info", Now.AddHours(-2)));
        var service = CreateService();

        var response = await service.GetStats(Request(Now.AddHours(-1), Now), Context());

        Assert.Equal(1, response.TotalLogs);
    }

    [Fact]
    public async Task GetStats_FiltersByServiceName()
    {
        await SeedAsync(
            Entry("Auth.API", "Error"),
            Entry("Payment.Gateway", "Info"));
        var service = CreateService();

        var response = await service.GetStats(Request(Now.AddHours(-1), Now, "Auth.API"), Context());

        Assert.Equal(1, response.TotalLogs);
        Assert.Equal("Auth.API", Assert.Single(response.Services).ServiceName);
    }
}
