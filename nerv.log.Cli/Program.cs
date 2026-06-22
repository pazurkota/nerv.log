// See https://aka.ms/new-console-template for more information

using Spectre.Console;

// nerv.log logo
string logo = """
                 ███╗   ██╗███████╗██████╗ ██╗   ██╗  ██╗      ██████╗  ██████╗ 
                 ████╗  ██║██╔════╝██╔══██╗██║   ██║  ██║     ██╔═══██╗██╔════╝ 
                 ██╔██╗ ██║█████╗  ██████╔╝██║   ██║  ██║     ██║   ██║██║  ███╗
                 ██║╚██╗██║██╔══╝  ██╔══██╗╚██╗ ██╔╝  ██║     ██║   ██║██║   ██║
                 ██║ ╚████║███████╗██║  ██║ ╚████╔╝██╗███████╗╚██████╔╝╚██████╔╝
                 ╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝  ╚═══╝ ╚═╝╚══════╝ ╚═════╝  ╚═════╝ 
                 """;

// welcome message
AnsiConsole.MarkupLine($"[Red3_1]{logo}[/]");
AnsiConsole.MarkupLine("Welcome to [Maroon]nerv.log v0.2[/] CLI tool!");
AnsiConsole.MarkupLine("Type [Maroon]nerv help[/] for more information.");