using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class VacuumCommand : AsyncCommand<DbVacuumSettings>
{
    protected override async Task<int> ExecuteAsync
        (CommandContext context, DbVacuumSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[DarkOrange3_1]Vacuum:[/] Running database vacuum...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]");
        AnsiConsole.MarkupLine($"Deleting logs older than [DarkOliveGreen3_1]{settings.OlderThan}d[/]" +
                               (settings.KeepErrors ? ", keeping Error/Critical logs" : string.Empty) + ".");

        using var channel = GrpcChannel.ForAddress(settings.Address);
        var client = new LogMaintenance.LogMaintenanceClient(channel);

        try
        {
            var request = new VacuumRequest { OlderThanDays = settings.OlderThan, KeepErrors = settings.KeepErrors };

            VacuumResponse response = await AnsiConsole.Status().StartAsync("Vacuuming logs...",
                async _ => await client.VacuumAsync(request, cancellationToken: cancellationToken));

            AnsiConsole.MarkupLine($"[DarkOrange3_1]Vacuum:[/] Deleted [DarkOliveGreen3_1]{response.DeletedCount}[/] " +
                                   $"log(s) in [DarkOliveGreen3_1]{response.DurationSeconds:F1}s[/].");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled ||
                                      cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[DarkOrange3_1]Vacuum:[/] Vacuum has been aborted by user.");
        }
        catch (RpcException ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Vacuum:[/] [red]Could not reach the server:[/] {ex.Status.Detail}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Vacuum:[/] Critical error occured: {ex.Message}.");
            return 1;
        }

        return 0;
    }
}
