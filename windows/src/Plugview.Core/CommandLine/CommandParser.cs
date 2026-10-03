using Plugview.Core.Selection;

namespace Plugview.Core.CommandLine;

/// <summary>What to show and how, as given on the command line.</summary>
public sealed record RunOptions
{
    /// <summary>Video device to use (name substring or unique ID).</summary>
    public string? VideoQuery { get; init; }

    /// <summary>Audio input to play (name substring or unique ID).</summary>
    public string? AudioQuery { get; init; }

    /// <summary>Whether to play the card's audio.</summary>
    public bool IsAudioEnabled { get; init; } = true;

    /// <summary>Start volume in percent.</summary>
    public long VolumePercent { get; init; } = 100;

    /// <summary>Whether to start in full screen.</summary>
    public bool StartsInFullScreen { get; init; }

    /// <summary>The capture mode to start in instead of the best one.</summary>
    public ModeChoice? Mode { get; init; }
}

/// <summary>The action requested on the command line.</summary>
public abstract record Command
{
    private Command() { }

    /// <summary>Open the window and play.</summary>
    public sealed record Run(RunOptions Options) : Command;

    /// <summary>Measure latency of the devices the options select, without opening a window.</summary>
    public sealed record Measure(RunOptions Options) : Command;

    /// <summary>List video and audio devices.</summary>
    public sealed record ListDevices : Command;

    /// <summary>Show the help text.</summary>
    public sealed record ShowHelp : Command;

    /// <summary>Show the version.</summary>
    public sealed record ShowVersion : Command;
}

/// <summary>A command line that cannot be run; the message is the one the spec prescribes.</summary>
public sealed class OptionsException : Exception
{
    /// <summary>Creates the error with its user-facing message.</summary>
    public OptionsException(string message) : base(message) { }

    /// <summary>Creates the error with no message.</summary>
    public OptionsException() { }

    /// <summary>Creates the error with a cause.</summary>
    public OptionsException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Reads the arguments that follow the executable name.</summary>
public static class CommandParser
{
    /// <summary>Parses <paramref name="arguments"/>.</summary>
    /// <exception cref="OptionsException">An unknown option, a missing or malformed value, or --audio with --no-audio.</exception>
    public static Command Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new RunOptions();
        var isMeasuring = false;
        using var remaining = arguments.GetEnumerator();

        while (remaining.MoveNext())
        {
            var argument = remaining.Current;
            switch (argument)
            {
                case "-h" or "--help": return new Command.ShowHelp();
                case "--version": return new Command.ShowVersion();
                case "-l" or "--list": return new Command.ListDevices();
                case "-d" or "--device": options = options with { VideoQuery = RequireValue(argument, remaining) }; break;
                case "-a" or "--audio": options = options with { AudioQuery = RequireValue(argument, remaining) }; break;
                case "--no-audio": options = options with { IsAudioEnabled = false }; break;
                case "--measure": isMeasuring = true; break;
                case "-f" or "--fullscreen": options = options with { StartsInFullScreen = true }; break;
                case "-v" or "--volume": options = options with { VolumePercent = RequireVolumePercent(argument, remaining) }; break;
                case "--mode": options = options with { Mode = RequireMode(argument, remaining) }; break;
                default: throw new OptionsException($"unknown option '{argument}'");
            }
        }

        if (!options.IsAudioEnabled && options.AudioQuery is not null)
        {
            throw new OptionsException("--audio and --no-audio cannot be combined");
        }
        return isMeasuring ? new Command.Measure(options) : new Command.Run(options);
    }

    private static string RequireValue(string option, IEnumerator<string> remaining)
    {
        if (!remaining.MoveNext() || remaining.Current.Length == 0 || remaining.Current.StartsWith('-'))
        {
            throw new OptionsException($"{option} needs a value");
        }
        return remaining.Current;
    }

    private static long RequireVolumePercent(string option, IEnumerator<string> remaining)
    {
        var value = RequireValue(option, remaining);
        return Numbers.WholeNumber(value) is { } percent && percent <= 100
            ? percent
            : throw new OptionsException($"{option} must be a whole number from 0 to 100, got '{value}'");
    }

    private static ModeChoice RequireMode(string option, IEnumerator<string> remaining)
    {
        var value = RequireValue(option, remaining);
        return ModeChoice.FromKey(value.ToLowerInvariant().Replace('×', 'x')) is { } mode
            ? mode
            : throw new OptionsException($"{option} must look like 1920x1080@60, got '{value}'");
    }
}
