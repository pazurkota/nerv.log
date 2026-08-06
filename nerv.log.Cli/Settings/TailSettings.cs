using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class TailSettings : CommandSettings
{
    [CommandOption("--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";

    [CommandOption("-s|--service")]
    [Description("Limit the stream to a single service name")]
    public string? Service { get; set; }

    [CommandOption("-e|--environment")]
    [Description("Limit the stream to a single environment")]
    public string? Environment { get; set; }

    [CommandOption("-l|--level")]
    [Description("Limit the stream to a single log level (e.g. Error, Warning)")]
    public string? Level { get; set; }
}