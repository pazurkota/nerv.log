using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class DbVacuumSettings : CommandSettings
{
    [CommandOption("-a|--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";

    [CommandOption("--older-than")]
    [Description("Clears logs older than provided time (in days)")]
    [DefaultValue(7)]
    public int OlderThan { get; set; } = 7;

    [CommandOption("--keep-errors")]
    [Description("Keep Error/Critical logs regardless of age")]
    [DefaultValue(false)]
    public bool KeepErrors { get; set; }
}
