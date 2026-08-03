using Grpc.Core;
using Grpc.Net.Client;
using nerv.log.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Commands;

public class TailCommand : AsyncCommand<TailSettings>
{
    protected override async Task<int> ExecuteAsync
        (CommandContext context, TailSettings settings, CancellationToken cancellationToken)
    {
        if (!TryParseLevel(settings.Level, out var level))
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Tail:[/] [red]Unknown level '{settings.Level}'.[/] " +
                                   $"Available levels: {string.Join(", ", Enum.GetNames<LogLevel>().Where(n => n != "Unspecified"))}.");
            return 1;
        }

        AnsiConsole.MarkupLine("[DarkOrange3_1]Tail:[/] Connecting...");
        AnsiConsole.MarkupLine($"Target gRPC server: [DarkOliveGreen3_1]{settings.Address}[/]" +
                               (settings.Service is null
                                   ? string.Empty
                                   : $", service: [DarkOliveGreen3_1]{settings.Service}[/]") +
                               (settings.Environment is null
                                   ? string.Empty
                                   : $", environment: [DarkOliveGreen3_1]{settings.Environment}[/]") +
                               (settings.Level is null
                                   ? string.Empty
                                   : $", level: [DarkOliveGreen3_1]{settings.Level}[/]"));

        using var channel = GrpcChannel.ForAddress(settings.Address);
        var client = new LogTail.LogTailClient(channel);

        var request = new TailRequest
        {
            ServiceName = settings.Service ?? string.Empty,
            Environment = settings.Environment ?? string.Empty,
            Level = level
        };

        try
        {
            using var call = client.Tail(request, cancellationToken: cancellationToken);
            AnsiConsole.MarkupLine("[DarkOrange3_1]Tail:[/] Connected. Streaming logs, press Ctrl+C to stop...");

            await foreach (var log in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                PrintLine(log);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled ||
                                      cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[DarkOrange3_1]Tail:[/] Stream has been stopped.");
        }
        catch (RpcException ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Tail:[/] [red]Could not reach the server:[/] {ex.Status.Detail}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[DarkOrange3_1]Tail:[/] Critical error occured: {ex.Message}.");
            return 1;
        }

        return 0;
    }

    private static void PrintLine(LogRequest log)
    {
        var timestamp = log.Timestamp?.ToDateTime().ToLocalTime().ToString("HH:mm:ss") ?? "--:--:--";
        var scope = string.IsNullOrEmpty(log.Environment) ? log.ServiceName : $"{log.ServiceName}@{log.Environment}";

        AnsiConsole.MarkupLine($"[Cyan3]{timestamp}[/] {Colorize(log.Level.ToString())} " +
                               $"[DarkOliveGreen3_1]{Markup.Escape(scope)}[/] {Markup.Escape(log.Message)}");
    }

    private static bool TryParseLevel(string? raw, out LogLevel level)
    {
        level = LogLevel.Unspecified;
        if (string.IsNullOrEmpty(raw)) return true;

        return Enum.TryParse(raw, ignoreCase: true, out level) && level != LogLevel.Unspecified;
    }

    private static string Colorize(string level) => level switch
    {
        "Error" => $"[red]{level}[/]",
        "Critical" => $"[red bold]{level}[/]",
        "Warning" => $"[yellow]{level}[/]",
        _ => level
    };
}
