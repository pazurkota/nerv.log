using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class TestAgentSettings : CommandSettings
{
    [CommandOption("-a|--address")]
    [Description("URL address of a gRPC service (default: http://localhost:8080)")]
    public string Address { get; set; } = "http://localhost:8080";
    
    [CommandOption("-d|--delay")]
    [Description("Delay (in ms) of a test logs to send")]
    public int DelayMs { get; set; }
}