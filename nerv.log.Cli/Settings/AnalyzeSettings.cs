using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class AnalyzeSettings : CommandSettings
{
    [CommandOption("--address")]
    [Description("URL address of a gRPC service")]
    [DefaultValue("http://localhost:8080")]
    public string Address { get; set; } = "http://localhost:8080";

    [CommandOption("-c|--check")]
    [Description("Analysis to run: stats, brute-force, error-spike or all")]
    [DefaultValue("all")]
    public string Check { get; set; } = "all";

    [CommandOption("-s|--service")]
    [Description("Limit the analysis to a single service name")]
    public string? Service { get; set; }

    [CommandOption("-e|--environment")]
    [Description("Limit the analysis to a single environment")]
    public string? Environment { get; set; }

    [CommandOption("-m|--metadata")]
    [Description("Limit the analysis to logs whose metadata contains this key=value pair")]
    public string? Metadata { get; set; }

    [CommandOption("-w|--window")]
    [Description("Time window to analyze (in minutes, counting back from now)")]
    [DefaultValue(60)]
    public int WindowMinutes { get; set; } = 60;

    [CommandOption("-t|--threshold")]
    [Description("Number of suspicious events within the window that raises a finding")]
    [DefaultValue(10)]
    public int Threshold { get; set; } = 10;
}
