// See https://aka.ms/new-console-template for more information

using nerv.log.Cli;
using Spectre.Console;
using Spectre.Console.Cli;

// nerv.log logo
string logo = """
                 ███╗   ██╗███████╗██████╗ ██╗   ██╗  ██╗      ██████╗  ██████╗ 
                 ████╗  ██║██╔════╝██╔══██╗██║   ██║  ██║     ██╔═══██╗██╔════╝ 
                 ██╔██╗ ██║█████╗  ██████╔╝██║   ██║  ██║     ██║   ██║██║  ███╗
                 ██║╚██╗██║██╔══╝  ██╔══██╗╚██╗ ██╔╝  ██║     ██║   ██║██║   ██║
                 ██║ ╚████║███████╗██║  ██║ ╚████╔╝██╗███████╗╚██████╔╝╚██████╔╝
                 ╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝  ╚═══╝ ╚═╝╚══════╝ ╚═════╝  ╚═════╝ 
                 """;

var app = new CommandApp();

app.Configure(CliConfig.ConfigureCommands);

// welcome message
AnsiConsole.MarkupLine($"[Red3_1]{logo}[/]");
AnsiConsole.MarkupLine("Welcome to [Maroon]nerv.log v0.2[/] CLI tool!");
AnsiConsole.MarkupLine("Type [Maroon]nerv help[/] for more information.");

return await app.RunAsync(args);