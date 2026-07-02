using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class FloodSettings : CommandSettings
{
    [CommandOption("-a|--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";

    [CommandOption("-d|--delay")]
    [Description("Delay (in ms) between test log entries")]
    [DefaultValue(1000)]
    public int DelayMs { get; set; } = 1000;
}