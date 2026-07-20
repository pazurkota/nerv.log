using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class AnalyzeCommand : AsyncCommand<AnalyzeSettings>
{
    private static readonly string[] KnownChecks = ["stats", "brute-force", "error-spike", "all"];

    private static readonly string[] LevelOrder =
        ["Trace", "Debug", "Info", "Warning", "Error", "Critical"];

    private const int BurstWindowSeconds = 60;

    protected override async Task<int> ExecuteAsync
        (CommandContext context, AnalyzeSettings settings, CancellationToken cancellationToken)
    {
        var check = settings.Check.ToLowerInvariant();
        if (!KnownChecks.Contains(check))
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] [red]Unknown check '{settings.Check}'.[/] " +
                                   $"Available checks: {string.Join(", ", KnownChecks)}.");
            return 1;
        }

        AnsiConsole.MarkupLine("[DarkOrange3_1]Analyze:[/] Running log analysis...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]");
        AnsiConsole.MarkupLine($"Check: [DarkOliveGreen3_1]{check}[/], window: " +
                               $"[DarkOliveGreen3_1]{settings.WindowMinutes}min[/]" +
                               (settings.Service is null
                                   ? string.Empty
                                   : $", service: [DarkOliveGreen3_1]{settings.Service}[/]"));

        using var channel = GrpcChannel.ForAddress(settings.Address);

        try
        {
            if (check is "stats" or "all")
            {
                await RunStatsAsync(channel, settings, cancellationToken);
            }

            if (check is "brute-force" or "all")
            {
                await RunBruteForceAsync(channel, settings, cancellationToken);
            }

            if (check is "error-spike" or "all")
            {
                AnsiConsole.MarkupLine("[DarkOrange3_1]Analyze:[/] [yellow]error-spike check is not implemented yet.[/]");
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled ||
                                      cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[DarkOrange3_1]Analyze:[/] Analysis has been aborted by user.");
        }
        catch (RpcException ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] [red]Could not reach the server:[/] {ex.Status.Detail}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] Critical error occured: {ex.Message}.");
            return 1;
        }

        return 0;
    }

    private static async Task RunStatsAsync
        (GrpcChannel channel, AnalyzeSettings settings, CancellationToken cancellationToken)
    {
        var client = new LogAnalytics.LogAnalyticsClient(channel);

        var to = DateTime.UtcNow;
        var from = to.AddMinutes(-settings.WindowMinutes);

        var request = new StatsRequest
        {
            From = Timestamp.FromDateTime(from),
            To = Timestamp.FromDateTime(to),
            ServiceName = settings.Service ?? string.Empty
        };

        StatsResponse stats = await AnsiConsole.Status().StartAsync("Fetching statistics...",
            async _ => await client.GetStatsAsync(request, cancellationToken: cancellationToken));

        if (stats.TotalLogs == 0)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] No logs found in the last " +
                                   $"[DarkOliveGreen3_1]{settings.WindowMinutes}min[/].");
            return;
        }

        var totalErrors = stats.Services.Sum(s => s.Errors);
        AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] Found [DarkOliveGreen3_1]{stats.TotalLogs}[/] logs, " +
                               $"overall error rate [DarkOliveGreen3_1]{Rate(totalErrors, stats.TotalLogs)}[/].");

        var levelTable = new Table().Title("Logs per level");
        levelTable.AddColumn("Level");
        levelTable.AddColumn(new TableColumn("Count").RightAligned());
        levelTable.AddColumn(new TableColumn("Share").RightAligned());

        foreach (var level in stats.Levels
                     .OrderBy(l => Array.IndexOf(LevelOrder, l.Level) is var i && i < 0 ? int.MaxValue : i))
        {
            levelTable.AddRow(Colorize(level.Level), level.Count.ToString(), Rate(level.Count, stats.TotalLogs));
        }

        AnsiConsole.Write(levelTable);

        var serviceTable = new Table().Title("Logs per service");
        serviceTable.AddColumn("Service");
        serviceTable.AddColumn(new TableColumn("Total").RightAligned());
        serviceTable.AddColumn(new TableColumn("Errors").RightAligned());
        serviceTable.AddColumn(new TableColumn("Error rate").RightAligned());

        foreach (var service in stats.Services)
        {
            serviceTable.AddRow(service.ServiceName, service.Total.ToString(),
                service.Errors.ToString(), Rate(service.Errors, service.Total));
        }

        AnsiConsole.Write(serviceTable);
    }

    private static async Task RunBruteForceAsync
        (GrpcChannel channel, AnalyzeSettings settings, CancellationToken cancellationToken)
    {
        var client = new LogAnalytics.LogAnalyticsClient(channel);

        var to = DateTime.UtcNow;
        var from = to.AddMinutes(-settings.WindowMinutes);

        var request = new BruteForceRequest
        {
            From = Timestamp.FromDateTime(from),
            To = Timestamp.FromDateTime(to),
            ServiceName = settings.Service ?? string.Empty,
            Threshold = settings.Threshold,
            BurstSeconds = BurstWindowSeconds
        };

        BruteForceResponse result = await AnsiConsole.Status().StartAsync("Scanning for brute-force bursts...",
            async _ => await client.GetBruteForceFindingsAsync(request, cancellationToken: cancellationToken));

        if (result.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] No brute-force bursts found " +
                                   $"([DarkOliveGreen3_1]≥{settings.Threshold}[/] failures within " +
                                   $"[DarkOliveGreen3_1]{BurstWindowSeconds}s[/]).");
            return;
        }

        AnsiConsole.MarkupLine($"[DarkOrange3_1]Analyze:[/] [red]Found {result.Findings.Count} suspicious burst(s)[/] " +
                               $"of [DarkOliveGreen3_1]≥{settings.Threshold}[/] failures within " +
                               $"[DarkOliveGreen3_1]{BurstWindowSeconds}s[/].");

        var table = new Table().Title("Brute-force findings");
        table.AddColumn("Service");
        table.AddColumn(new TableColumn("Failures").RightAligned());
        table.AddColumn("Window start");
        table.AddColumn("Window end");

        foreach (var finding in result.Findings.OrderByDescending(f => f.FailureCount))
        {
            table.AddRow(finding.ServiceName, finding.FailureCount.ToString(),
                finding.WindowStart.ToDateTime().ToLocalTime().ToString("HH:mm:ss"),
                finding.WindowEnd.ToDateTime().ToLocalTime().ToString("HH:mm:ss"));
        }

        AnsiConsole.Write(table);
    }

    private static string Rate(long part, long total) =>
        total == 0 ? "0.0%" : $"{100.0 * part / total:F1}%";

    private static string Colorize(string level) => level switch
    {
        "Error" => $"[red]{level}[/]",
        "Critical" => $"[red bold]{level}[/]",
        "Warning" => $"[yellow]{level}[/]",
        _ => level
    };
}
