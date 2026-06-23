using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli;

public class CliInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (context.Name == "help" || context.Remaining.Raw.Contains("--help") || context.Remaining.Raw.Contains("-h"))
        {
            return;
        }

        PrintLogo();
    }

    public static void PrintLogo()
    {
        string logo = """
                       ███╗   ██╗███████╗██████╗ ██╗   ██╗  ██╗      ██████╗  ██████╗
                       ████╗  ██║██╔════╝██╔══██╗██║   ██║  ██║     ██╔═══██╗██╔════╝
                       ██╔██╗ ██║█████╗  ██████╔╝██║   ██║  ██║     ██║   ██║██║  ███╗
                       ██║╚██╗██║██╔══╝  ██╔══██╗╚██╗ ██╔╝  ██║     ██║   ██║██║   ██║
                       ██║ ╚████║███████╗██║  ██║ ╚████╔╝██╗███████╗╚██████╔╝╚██████╔╝
                       ╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝  ╚═══╝ ╚═╝╚══════╝ ╚═════╝  ╚═════╝
                      """;

        AnsiConsole.MarkupLine($"[Red3_1]{logo}[/]");
        AnsiConsole.MarkupLine("Welcome to [Maroon]nerv.log v0.2[/] CLI tool!");
        AnsiConsole.WriteLine();
    }
}