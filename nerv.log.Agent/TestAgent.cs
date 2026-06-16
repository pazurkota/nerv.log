using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Services;

namespace nerv.log.Agent;

public class TestAgent
    (string serverAddress, int delayMs, Random random)
{
    private readonly string[] _levels = ["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"];
    private readonly string[] _services = ["Auth.API", "Payment.Gateway", "Inventory.Worker"];
    private readonly string[] _messages =
    [
        "User successfully authenticated",
        "Connection timeout while connecting to database",
        "Payment processed for order #1429",
        "Cache miss for configuration key",
        "NullReferenceException in TransactionController.cs:42"
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"agent: Connecting to {serverAddress}...");

        using var channel = GrpcChannel.ForAddress(serverAddress);
        var client = new LogIngestion.LogIngestionClient(channel);

        try
        {
            using var streamingCall = client.StreamLogs(cancellationToken: cancellationToken);
            Console.WriteLine($"agent: Connected. Starting steaming data with {delayMs}ms delay.");

            while (!cancellationToken.IsCancellationRequested)
            {
                //@TODO: Create random log stream to nerv.log
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled ||
                                      cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("agent: Stream has been aborted by user.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"agent: Critical error occured: {ex.Message}.");
        }
    }

    private LogRequest GenerateRandomLog()
    {
        // @TODO: Finish this function
        return new LogRequest
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}