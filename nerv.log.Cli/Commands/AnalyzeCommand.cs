using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class AnalyzeCommand : AsyncCommand<AnalyzeSettings>
{
    protected override Task<int> ExecuteAsync
        (CommandContext context, AnalyzeSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[DarkOrange3_1]Analyze:[/] Running log analysis...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]");
        AnsiConsole.MarkupLine($"Check: [DarkOliveGreen3_1]{settings.Check}[/], window: " +
                               $"[DarkOliveGreen3_1]{settings.WindowMinutes}min[/], threshold: " +
                               $"[DarkOliveGreen3_1]{settings.Threshold}[/]" +
                               (settings.Service is null
                                   ? string.Empty
                                   : $", service: [DarkOliveGreen3_1]{settings.Service}[/]"));

        // TODO: fetch aggregated log data from the server and run the selected checks:
        //  - stats:       totals per level/service, error rate, busiest services
        //  - brute-force: repeated authentication failures from the same source within the window
        //  - error-spike: sudden rise of Error/Critical entries compared to the baseline
        AnsiConsole.MarkupLine("[DarkOrange3_1]Analyze:[/] [yellow]Not implemented yet.[/] " +
                               "Planned checks are listed below.");

        var table = new Table();
        table.AddColumn("Check");
        table.AddColumn("Description");
        table.AddRow("stats", "Log volume and error-rate statistics per service and level");
        table.AddRow("brute-force", "Repeated authentication failures exceeding the threshold within the window");
        table.AddRow("error-spike", "Abnormal growth of Error/Critical entries against the baseline");
        AnsiConsole.Write(table);

        return Task.FromResult(0);
    }
}
