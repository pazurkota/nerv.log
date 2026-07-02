using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class SpikeSettings : CommandSettings
{
    [CommandOption("-a|--amount")]
    [Description("Amount of logs sended during spike")]
    [DefaultValue(50000)]
    public int LogAmount { get; set; } = 50000;

    [CommandOption("-s|--spike")]
    [Description("Spike duration (in seconds)")]
    [DefaultValue(2)]
    public int SpikeDuration { get; set; } = 2;

    [CommandOption("-r|--rest")] 
    [Description("Rest duration (in seconds)")]
    [DefaultValue(10)]
    public int RestDuration { get; set; } = 10;
}