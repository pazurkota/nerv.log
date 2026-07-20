using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class VacuumCommand : AsyncCommand<VacuumSettings>
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    protected override async Task<int> ExecuteAsync
        (CommandContext context, VacuumSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[DarkOrange3_1]Vacuum:[/] Running database vacuum...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]");

        using var channel = GrpcChannel.ForAddress(settings.Address);
        var client = new LogMaintenance.LogMaintenanceClient(channel);

        try
        {
            var request = new VacuumRequest { Full = settings.Full };

            VacuumResponse response = await AnsiConsole.Status().StartAsync(
                settings.Full ? "Running VACUUM FULL (this may take a while)..." : "Running VACUUM...",
                async _ => await client.VacuumAsync(request, cancellationToken: cancellationToken));

            var reclaimed = response.SizeBeforeBytes - response.SizeAfterBytes;

            AnsiConsole.MarkupLine($"[DarkOrange3_1]Vacuum:[/] Done in [DarkOliveGreen3_1]{response.DurationSeconds:F1}s[/]. " +
                                   $"Size before: [DarkOliveGreen3_1]{FormatBytes(response.SizeBeforeBytes)}[/], " +
                                   $"after: [DarkOliveGreen3_1]{FormatBytes(response.SizeAfterBytes)}[/]" +
                                   (reclaimed > 0
                                       ? $", reclaimed [DarkOliveGreen3_1]{FormatBytes(reclaimed)}[/]."
                                       : "."));
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

    private static string FormatBytes(long bytes)
    {
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < SizeUnits.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F1}{SizeUnits[unit]}";
    }
}
