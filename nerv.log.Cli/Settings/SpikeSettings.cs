using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class SpikeSettings : CommandSettings
{
    [CommandOption("--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";

    [CommandOption("-a|--amount")]
    [Description("Amount of logs sended during spike")]
    [DefaultValue(20000)]
    public int LogAmount { get; set; } = 20000;

    [CommandOption("-s|--spike")]
    [Description("Spike duration (in seconds)")]
    [DefaultValue(2)]
    public int SpikeDuration { get; set; } = 2;

    [CommandOption("-r|--rest")] 
    [Description("Rest duration (in seconds)")]
    [DefaultValue(10)]
    public int RestDuration { get; set; } = 10;
}