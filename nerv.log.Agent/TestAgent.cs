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
}