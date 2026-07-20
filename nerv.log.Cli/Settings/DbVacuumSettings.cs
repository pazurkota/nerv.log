using System.ComponentModel;
using Spectre.Console.Cli;

namespace nerv.log.Cli.Settings;

public class DbVacuumSettings : CommandSettings
{
    [CommandOption("--older-than")]
    [Description("Clears logs older than provided time (in days)")]
    [DefaultValue("7")]
    public static int OlderThan { get; set; } = 7;

    [CommandOption("--keep-errors")]
    [Description("Keep errors in database")]
    [DefaultValue("false")]
    public static bool KeepErrors { get; set; } = false;
}