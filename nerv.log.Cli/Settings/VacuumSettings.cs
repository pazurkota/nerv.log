using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class VacuumSettings : CommandSettings
{
    [CommandOption("-a|--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";

    [CommandOption("-f|--full")]
    [Description("Run VACUUM FULL to reclaim maximum disk space (locks the table exclusively while running)")]
    [DefaultValue(false)]
    public bool Full { get; set; }
}
