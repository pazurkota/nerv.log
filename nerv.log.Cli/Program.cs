using nerv.log.Cli;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(CliConfig.ConfigureCommands);

if (args.Length == 0)
{
    CliInterceptor.PrintLogo();
}

return await app.RunAsync(args);