using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class SpikeCommand : AsyncCommand<SpikeSettings>
{
    private readonly LogLevel[] _levels =
        [LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error, LogLevel.Critical];

    private readonly string[] _services = ["Auth.API", "Payment.Gateway", "Inventory.Worker"];
    private readonly string[] _messages =
    [
        "User successfully authenticated",
        "Connection timeout while connecting to database",
        "Payment processed for order #1429",
        "Cache miss for configuration key",
        "NullReferenceException in TransactionController.cs:42"
    ];

    private readonly Random _random = new();

    protected override async Task<int> ExecuteAsync
        (CommandContext context, SpikeSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[DarkOrange3_1]Spike:[/] Running spike test...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]");

        using var channel = GrpcChannel.ForAddress(settings.Address);
        var client = new LogIngestion.LogIngestionClient(channel);

        var totalSent = 0L;
        var waveNumber = 0;

        try
        {
            using var streamingCall = client.StreamLogs();
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Spike:[/] Connected. Bursting " +
                                   $"[DarkOliveGreen3_1]{settings.LogAmount}[/] logs over " +
                                   $"[DarkOliveGreen3_1]{settings.SpikeDuration}s[/], resting " +
                                   $"[DarkOliveGreen3_1]{settings.RestDuration}s[/] between waves.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    waveNumber++;
                    totalSent += await SendWaveAsync(streamingCall, settings, waveNumber, cancellationToken);

                    if (cancellationToken.IsCancellationRequested) break;

                    await RestAsync(settings.RestDuration, waveNumber, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

            await streamingCall.RequestStream.CompleteAsync();
            var response = await streamingCall.ResponseAsync;
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Spike:[/] Stream completed. Sent {totalSent} logs across " +
                                   $"{waveNumber} wave(s), server processed {response.LogsProcessed} logs.");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled ||
                                      cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[DarkOrange3_1]Spike:[/] Stream has been aborted by user.");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Spike:[/] Critical error occured: {ex.Message}.");
        }

        return 0;
    }

    private async Task<long> SendWaveAsync(
        AsyncClientStreamingCall<LogRequest, IngestionResponse> streamingCall,
        SpikeSettings settings,
        int waveNumber,
        CancellationToken cancellationToken)
    {
        var sent = 0L;
        var stopwatch = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(settings.SpikeDuration);

        await AnsiConsole.Status().StartAsync($"Wave {waveNumber}: spiking...", async ctx =>
        {
            while (sent < settings.LogAmount &&
                   stopwatch.Elapsed < deadline &&
                   !cancellationToken.IsCancellationRequested)
            {
                await streamingCall.RequestStream.WriteAsync(GenerateRandomLog(), cancellationToken);
                sent++;

                if (sent % 500 == 0)
                {
                    ctx.Status($"Wave {waveNumber}: sent [DarkOliveGreen3_1]{sent}[/]/{settings.LogAmount} logs " +
                               $"({stopwatch.Elapsed.TotalSeconds:F1}s elapsed)");
                }
            }
        });

        stopwatch.Stop();
        AnsiConsole.MarkupLine($"[DarkOrange3_1]Spike:[/] Wave {waveNumber} sent " +
                               $"[DarkOliveGreen3_1]{sent}[/] logs in {stopwatch.Elapsed.TotalSeconds:F2}s.");
        return sent;
    }

    private static async Task RestAsync(int restDuration, int waveNumber, CancellationToken cancellationToken)
    {
        try
        {
            await AnsiConsole.Status().StartAsync($"Wave {waveNumber} done. Resting...", async ctx =>
            {
                for (var remaining = restDuration; remaining > 0; remaining--)
                {
                    ctx.Status($"Waiting for next wave... [DarkOliveGreen3_1]{remaining}s[/] remaining");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private LogRequest GenerateRandomLog()
    {
        return new LogRequest
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Level = _levels[_random.Next(_levels.Length)],
            ServiceName = _services[_random.Next(_services.Length)],
            Message = _messages[_random.Next(_messages.Length)]
        };
    }
}
