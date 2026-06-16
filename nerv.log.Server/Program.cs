using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using nerv.log.Database;
using nerv.log.Model;
using nerv.log.Services;
using nerv.log.Workers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddSingleton(Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait
}));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHostedService<LogStorageWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var context = await db.CreateDbContextAsync();
    await context.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.MapGrpcService<LogIngestionService>();
app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();