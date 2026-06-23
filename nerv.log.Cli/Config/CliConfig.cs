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

        config.AddCommand<TestAgentCommand>("agent")
            .WithDescription("CLI test agent for gRPC testing");
    }
}