// See https://aka.ms/new-console-template for more information

using nerv.log.Agent;

Console.WriteLine("nerv.log.Server CLI test agent");

// default url address from docker
string serverAddr = args.Length > 0 ? args[0] : "http://localhost:8080"; 
int delayMs = args.Length > 1 && int.TryParse(args[1], out var parsedDelay) ? parsedDelay : 1000;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.WriteLine("agent: Cancellation signal received, stopping agent...");
    cts.Cancel();
    eventArgs.Cancel = true;
};

var agent = new TestAgent(serverAddr, delayMs, new Random());
await agent.StartAsync(cts.Token);

Console.WriteLine("worker: Test agent has been disabled.");