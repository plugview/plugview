using System.Globalization;
using Plugview.Core;
using Plugview.Core.CommandLine;
using Plugview.Core.Measurement;
using Plugview.Core.Selection;
using Plugview.Windows.Audio;
using Plugview.Windows.Devices;
using Plugview.Windows.Interop;
using Plugview.Windows.Video;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Plugview.Windows.Playback;

/// <summary>The command's three jobs on Windows: list devices, play in a window, and measure (spec: "Command").</summary>
public static class Player
{
    private const int ExitFailure = 1;

    /// <summary><c>plugview --list</c>, in the spec's layout.</summary>
    public static async Task<int> ListAsync(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var videos = await DeviceCatalog.VideoDevicesAsync();
        var audios = await DeviceCatalog.AudioInputsAsync(videos);
        output.WriteLine("Video devices:");
        if (videos.Count == 0) output.WriteLine("  (none)");
        foreach (var video in videos)
        {
            output.WriteLine($"  {video.Name}  [{TransportName(video.Transport)}]  id: {video.UniqueId}");
            var modes = VideoModes.PickableModes(DeviceCatalog.Modes(video));
            if (modes.Count > 0) output.WriteLine($"    modes: {string.Join(", ", modes.Select(mode => mode.Key))}");
        }
        output.WriteLine();
        output.WriteLine("Audio inputs:");
        if (audios.Count == 0) output.WriteLine("  (none)");
        foreach (var audio in audios) output.WriteLine($"  {audio.Name}  [{TransportName(audio.Transport)}]  id: {audio.UniqueId}");
        return 0;
    }

    /// <summary>Plays the selected card in a window until it closes.</summary>
    public static async Task<int> RunAsync(RunOptions options, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var (video, audioInput) = await SelectDevicesAsync(options, error);
        if (video is null) return ExitFailure;

        using var presenter = new VideoPresenter();
        var window = new PlayerWindow("plugview", 1280, 720);
        var (width, height) = window.ClientSize;
        presenter.Attach(window.Handle, width, height);
        window.Resized += presenter.Resize;

        VideoCapture capture;
        try
        {
            capture = new VideoCapture(video, options.Mode, presenter);
        }
        catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            error.WriteLine($"plugview: {CaptureErrors.Describe(failure, video.Name)}");
            return ExitFailure;
        }
        using (capture)
        {
            using var audio = StartAudio(audioInput, video, options, error);
            var volumePercent = options.VolumePercent;
            var isMuted = false;
            void ApplyVolume()
            {
                if (audio is not null) audio.Volume = isMuted ? 0 : volumePercent / 100f;
            }
            ApplyVolume();

            window.KeyPressed += key =>
            {
                switch (key)
                {
                    case PlayerKey.ToggleFullScreen: window.ToggleFullScreen(); break;
                    case PlayerKey.LeaveFullScreen when window.IsFullScreen: window.ToggleFullScreen(); break;
                    case PlayerKey.LeaveFullScreen: break;
                    case PlayerKey.ToggleMute:
                        isMuted = !isMuted;
                        ApplyVolume();
                        output.WriteLine(isMuted ? "Muted" : $"Volume {volumePercent}%");
                        break;
                    case PlayerKey.VolumeUp or PlayerKey.VolumeDown:
                        volumePercent = Math.Clamp(volumePercent + (key == PlayerKey.VolumeUp ? Tuning.VolumeStepPercent : -Tuning.VolumeStepPercent), 0, 100);
                        isMuted = false;
                        ApplyVolume();
                        output.WriteLine($"Volume {volumePercent}%");
                        break;
                    case PlayerKey.Quit: window.Close(); break;
                }
            };
            capture.Stopped += failure =>
            {
                error.WriteLine($"plugview: {CaptureErrors.Describe(failure, video.Name)}");
                window.Close();
            };

            var audioSummary = audio is null ? "no audio" : $"audio from {audio.InputName}";
            output.WriteLine($"Showing {video.Name} · {capture.Mode.Summary}, {audioSummary}");
            window.SetTitle($"{video.Name} · {capture.Mode.Summary}");
            capture.Start();
            audio?.Start();
            if (options.StartsInFullScreen) window.ToggleFullScreen();
            window.RunUntilClosed();
        }
        return 0;
    }

    /// <summary><c>plugview --measure</c>: frame rate and latency for a few seconds, with advice worded for Windows.</summary>
    public static async Task<int> MeasureAsync(RunOptions options, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var (video, audioInput) = await SelectDevicesAsync(options, error);
        if (video is null) return ExitFailure;

        output.WriteLine($"Measuring {video.Name} for {(int)Tuning.MeasurementSeconds} seconds…");
        using var presenter = new VideoPresenter();
        VideoCapture capture;
        try
        {
            capture = new VideoCapture(video, options.Mode, presenter);
        }
        catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            error.WriteLine($"plugview: {CaptureErrors.Describe(failure, video.Name)}");
            return ExitFailure;
        }

        var gate = new Lock();
        var videoLatencies = new List<double>();
        var intervals = new List<double>();
        var audioLatencies = new List<double>();
        long? lastCapture = null;
        var warmUpEnd = Clock.Now100ns() + 5_000_000;
        using (capture)
        {
            capture.FrameArrived += timing =>
            {
                if (Clock.Now100ns() < warmUpEnd) return;
                lock (gate)
                {
                    videoLatencies.Add(timing.CaptureToAppMilliseconds);
                    if (lastCapture is { } previous) intervals.Add((timing.CaptureTime100ns - previous) / 10_000.0);
                    lastCapture = timing.CaptureTime100ns;
                }
            };
            using var audio = StartAudio(audioInput, video, options, error);
            if (audio is not null)
            {
                // Measured, not heard: --measure only listens.
                audio.Volume = 0;
                audio.PacketArrived += latency =>
                {
                    if (Clock.Now100ns() < warmUpEnd) return;
                    lock (gate) audioLatencies.Add(latency);
                };
            }
            capture.Start();
            audio?.Start();
            await Task.Delay(TimeSpan.FromSeconds(Tuning.MeasurementSeconds));

            lock (gate) Report(output, video, capture.Mode, videoLatencies, intervals, audio, audioLatencies);
        }
        return 0;
    }

    private static void Report(
        TextWriter output, DeviceInfo video, VideoMode mode, List<double> videoLatencies, List<double> intervals, AudioPassthrough? audio, List<double> audioLatencies)
    {
        var medianInterval = LatencyAdvice.Percentile(intervals, 0.5) ?? 0;
        var delivered = medianInterval > 0 ? 1000 / medianInterval : 0;
        output.WriteLine();
        output.WriteLine($"Video    {video.Name} · {mode.Width}×{mode.Height} · {EncodingName(mode.Encoding)}");
        output.WriteLine($"         device set to {Format(mode.MaxFrameRate)} fps, delivered {Format(delivered)} fps ({videoLatencies.Count} frames)");
        output.WriteLine($"         frame interval  {Spread(intervals)}");
        output.WriteLine($"         capture → app   {Spread(videoLatencies)}");
        if (audio is null)
        {
            output.WriteLine("Audio    not measured (no audio input selected)");
        }
        else
        {
            output.WriteLine($"Audio    {audio.InputName}");
            output.WriteLine($"         capture → app   {Spread(audioLatencies)}");
        }
        output.WriteLine(audio is null ? "Output   unknown" : $"Output   {audio.OutputName} · reports {Format(audio.OutputLatencyMilliseconds)} ms");
        var refreshRate = DisplayRefreshRate();
        output.WriteLine($"Display  {(refreshRate is { } hertz ? $"{Format(hertz)} Hz" : "unknown")}");

        var facts = new LatencyFacts(mode.MaxFrameRate, delivered, refreshRate, audio?.OutputLatencyMilliseconds, audio?.IsOutputBluetooth ?? false);
        var advice = LatencyAdvice.Advice(facts);
        output.WriteLine();
        output.WriteLine(advice.Count == 0 ? "Everything plugview controls is at its fastest setting." : "To lower latency:");
        foreach (var line in advice) output.WriteLine($"  • {line}");
    }

    /// <summary>The spec's device choice, waiting up to <see cref="Tuning.CardAudioWaitSeconds"/> for the card's audio.</summary>
    private static async Task<(DeviceInfo? Video, DeviceInfo? Audio)> SelectDevicesAsync(RunOptions options, TextWriter error)
    {
        var videos = await DeviceCatalog.VideoDevicesAsync();
        var video = DeviceSelection.SelectVideoDevice(videos, options.VideoQuery);
        if (video is null)
        {
            error.WriteLine("plugview: no capture device found. Run 'plugview --list' to see detected devices.");
            return (null, null);
        }
        if (!options.IsAudioEnabled) return (video, null);

        var deadline = DateTime.UtcNow.AddSeconds(Tuning.CardAudioWaitSeconds);
        do
        {
            var audio = DeviceSelection.SelectAudioDevice(await DeviceCatalog.AudioInputsAsync(videos), video, options.AudioQuery);
            if (audio is not null) return (video, audio);
            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);
        error.WriteLine($"plugview: no audio input matched {video.Name}; playing without sound. Run 'plugview --list' and pass '--audio <name>'.");
        return (video, null);
    }

    private static AudioPassthrough? StartAudio(DeviceInfo? audioInput, DeviceInfo video, RunOptions options, TextWriter error)
    {
        if (audioInput is null || !options.IsAudioEnabled) return null;
        try
        {
            return new AudioPassthrough(audioInput.UniqueId, video.UniqueId);
        }
        catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            error.WriteLine($"plugview: no sound: {CaptureErrors.Describe(failure, audioInput.Name)}");
            return null;
        }
    }

    private static unsafe double? DisplayRefreshRate()
    {
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return PInvoke.EnumDisplaySettings(null, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode) ? mode.dmDisplayFrequency : null;
    }

    private static string Spread(List<double> milliseconds)
    {
        if (LatencyAdvice.Percentile(milliseconds, 0.5) is not { } median) return "no samples";
        var p95 = LatencyAdvice.Percentile(milliseconds, 0.95) ?? median;
        var worst = LatencyAdvice.Percentile(milliseconds, 1) ?? median;
        return $"median {Format(median)} ms · p95 {Format(p95)} ms · worst {Format(worst)} ms";
    }

    private static string Format(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string EncodingName(FrameEncoding encoding) => encoding switch
    {
        FrameEncoding.Native => "uncompressed (native)",
        FrameEncoding.Converted => "uncompressed, converted by Windows",
        FrameEncoding.Compressed => "compressed (decoded by Windows)",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unhandled encoding"),
    };

    /// <summary>The transport's case name, as the Mac prints it.</summary>
    private static string TransportName(DeviceTransport transport) => transport switch
    {
        DeviceTransport.BuiltIn => "builtIn",
        DeviceTransport.Usb => "usb",
        DeviceTransport.Virtual => "virtual",
        DeviceTransport.Wireless => "wireless",
        DeviceTransport.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unhandled transport"),
    };
}
