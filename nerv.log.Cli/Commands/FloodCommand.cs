using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class FloodCommand : AsyncCommand<FloodSettings>
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
    
    protected override async Task<int> ExecuteAsync
        (CommandContext context, FloodSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[DarkOrange3_1]Flood:[/] Running flood...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]");

        using var channel = GrpcChannel.ForAddress(settings.Address);
        var client = new LogIngestion.LogIngestionClient(channel);

        try
        {
            using var streamingCall = client.StreamLogs();
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Flood:[/] Connected. " +
                                   $"Starting steaming data with [DarkOliveGreen3_1]{settings.DelayMs}ms[/] delay.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var logRequest = GenerateRandomLog();

                    await streamingCall.RequestStream.WriteAsync(logRequest, cancellationToken);
                    AnsiConsole.MarkupLine($"[Cyan3]({DateTime.Now:HH:mm:ss zz})[/] -> " +
                                           $"{logRequest.Level} | {logRequest.ServiceName} | {logRequest.Message}");

                    await Task.Delay(settings.DelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

            await streamingCall.RequestStream.CompleteAsync();
            var response = await streamingCall.ResponseAsync;
            AnsiConsole.MarkupLine($"flood: Stream completed. Server processed {response.LogsProcessed} logs.");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled ||
                                      cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[DarkOrange3_1]Flood:[/] Stream has been aborted by user.");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Flood:[/] Critical error occured: {ex.Message}.");
        }

        return 0;
    }
    
    private LogRequest GenerateRandomLog()
    {
        Random random = new Random();
        
        return new LogRequest
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Level = _levels[random.Next(_levels.Length)],
            ServiceName = _services[random.Next(_services.Length)],
            Message = _messages[random.Next(_messages.Length)]
        };
    }
}