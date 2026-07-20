using nerv.log.Cli.Commands;
using Spectre.Console.Cli;

namespace nerv.log.Cli;

public class CliConfig
{
    public static void ConfigureCommands(IConfigurator config)
    {
        config.SetApplicationName("nerv");
        config.SetApplicationVersion("v0.2");
        
        config.SetInterceptor(new CliInterceptor());

#if DEBUG
        config.PropagateExceptions();
#endif

        config.AddCommand<FloodCommand>("flood")
            .WithDescription("Continuously streams test logs to a gRPC server");

        config.AddCommand<SpikeCommand>("spike")
            .WithDescription("Sends bursts of logs in short spikes with rest periods in between");

        config.AddCommand<AnalyzeCommand>("analyze")
            .WithDescription("Analyzes stored logs for statistics and common attack patterns (e.g. brute-force)");
    }
}