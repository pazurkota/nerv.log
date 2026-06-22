using nerv.log.Cli.Commands;
using Spectre.Console.Cli;

namespace nerv.log.Cli;

public class CliConfig
{
    public static void ConfigureCommands(IConfigurator config)
    {
        config.SetApplicationName("nerv.log");
        config.SetApplicationVersion("v0.2");

#if DEBUG
        config.PropagateExceptions();
#endif

        config.AddBranch("agent", agent =>
        {
            agent.SetDescription("CLI test agent for gRPC testing");
            agent.AddCommand<TestAgentCommand>("agent")
                .WithDescription("Test agent");
        });

        config.SetInterceptor(new CliInterceptor());
    }
}