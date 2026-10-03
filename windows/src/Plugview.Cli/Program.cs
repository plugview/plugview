using System.Text;
using Plugview.Cli;
using Plugview.Core;
using Plugview.Core.CommandLine;
using Plugview.Windows.App;
using Plugview.Windows.Playback;

// sysexits.h EX_USAGE, as on the Mac (spec: "Command").
const int usageExitCode = 64;

Console.OutputEncoding = Encoding.UTF8;
Command command;
try
{
    command = CommandParser.Parse(args);
}
catch (OptionsException error)
{
    Console.Error.WriteLine($"plugview: {error.Message}");
    Console.Error.WriteLine("Run 'plugview --help' for usage.");
    return usageExitCode;
}

switch (command)
{
    case Command.ShowHelp:
        Console.WriteLine(Usage.Text);
        return 0;
    case Command.ShowVersion:
        Console.WriteLine(Tuning.PlugviewVersion);
        return 0;
    case Command.ListDevices:
        return await Player.ListAsync(Console.Out);
    case Command.Measure measure:
        return await Player.MeasureAsync(measure.Options, Console.Out, Console.Error);
    case Command.Run run:
        // The same window as the app (launcher, controls, signal watch), as the command: closing it quits.
        return AppHost.RunCommand(run.Options);
    default:
        throw new InvalidOperationException($"unhandled command {command}");
}
