using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class TailSettings : CommandSettings
{
    [CommandOption("--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";
}