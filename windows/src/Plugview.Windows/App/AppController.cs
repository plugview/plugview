using System.Runtime.InteropServices;
using Plugview.Core;
using Plugview.Core.CommandLine;
using Plugview.Core.Launcher;
using Plugview.Core.Selection;
using Plugview.Windows.Audio;
using Plugview.Windows.Devices;
using Plugview.Windows.Graphics;
using Plugview.Windows.Playback;
using Plugview.Windows.Ui;
using Plugview.Windows.Video;

namespace Plugview.Windows.App;

/// <summary>How plugview was started.</summary>
internal enum LaunchMode
{
    /// <summary>From a terminal (<c>plugview</c>): the command, exactly as on the Mac; closing the window quits.</summary>
    Command,

    /// <summary>From the Start menu: one instance with a tray icon that remembers the user's choices.</summary>
    App,
}

/// <summary>A playing card: its capture, its sound and how it plays.</summary>
internal sealed class Playback(DeviceInfo device, VideoCapture capture, AudioPassthrough? audio) : IDisposable
{
    public DeviceInfo Device { get; } = device;

    public VideoCapture Capture { get; } = capture;

    public AudioPassthrough? Audio { get; } = audio;

    public VideoMode Mode => Capture.Mode;

    public void Dispose()
    {
        Audio?.Dispose();
        Capture.Dispose();
    }
}

/// <summary>
/// The app: one window that shows the launcher until a card plays, then the video, driven by the shared launcher state
/// machine (<see cref="LauncherTransitions"/>) exactly as on the Mac and Linux.
/// </summary>
internal sealed partial class AppController : IWindowContent, IDisposable
{
    private const double ScreenFadeSeconds = 0.32;

    private readonly AppWindow window;
    private readonly UiThread uiThread;
    private readonly VideoPresenter presenter;
    private readonly DeviceWatch deviceWatch;
    private readonly Settings settings;
    private readonly LaunchMode launchMode;
    private readonly Dictionary<string, IReadOnlyList<VideoMode>> modesByDevice = [];
    private RunOptions options;
    private LauncherState state = new LauncherState.WaitingForSignal();
    private IReadOnlyList<DeviceInfo> videoDevices = [];
    private IReadOnlyList<DeviceInfo> playable = [];
    private Playback? playback;
    private bool isStarting;
    private bool isRefreshing;
    private bool isRefreshAgain;
    private int volumePercent;
    private bool isMuted;
    private bool isCloseKeyDown;
    private string screenId = "";
    private double screenSince;
    private (LauncherModel Model, double Since)? fadingScreen;
    private double fadingSince;
    private readonly long startTicks = Environment.TickCount64;

    public AppController(UiThread uiThread, RunOptions options, LaunchMode launchMode)
    {
        this.uiThread = uiThread;
        this.options = options;
        this.launchMode = launchMode;
        settings = Settings.Load();
        // Volume is not remembered (spec: "Remembered choices"): every launch starts at --volume or 100 %.
        volumePercent = (int)options.VolumePercent;

        window = new AppWindow(uiThread, launchMode == LaunchMode.App ? AppWindow.AppClassName : AppWindow.CommandClassName, "plugview", 1280, 760);
        if (settings.WindowFrame is [var left, var top, var width, var height]) window.RestoredFrame = (left, top, width, height);
        presenter = new VideoPresenter();
        var (clientWidth, clientHeight) = window.ClientSize;
        presenter.Attach(window.Handle, Math.Max(clientWidth, 1), Math.Max(clientHeight, 1));
        window.Resized += presenter.Resize;
        window.PowerChanged += OnPowerChanged;
        window.Content = this;
        Accessibility = new AccessibilityProvider(window);
        if (settings.KeepsOnTop && launchMode == LaunchMode.App) window.SetTopmost(true);

        deviceWatch = new DeviceWatch(uiThread);
        deviceWatch.Changed += () => _ = RefreshDevicesAsync();
        StartTray();
    }

    /// <summary>Narrator's view of the window.</summary>
    private AccessibilityProvider Accessibility { get; }

    /// <summary>Opens the window (unless <paramref name="startsHidden"/>) and starts looking for cards.</summary>
    public void Start(bool startsHidden)
    {
        // At login plugview waits in the tray; without a tray there would be no way back to it, so it shows.
        if (!(startsHidden && HasTray)) window.Show();
        if (options.StartsInFullScreen) window.ToggleFullScreen();
        var cameraStatus = Permissions.Status(PermissionKind.Camera);
        if (cameraStatus == PermissionStatus.Denied)
        {
            Send(new LauncherEvent.PermissionDenied(PermissionKind.Camera));
            return;
        }
        if (cameraStatus == PermissionStatus.WillPrompt && !settings.HasSeenPermissions)
        {
            Send(new LauncherEvent.PermissionRequested(PermissionKind.Camera));
            return;
        }
        _ = RefreshDevicesAsync();
    }

    private double Now => (Environment.TickCount64 - startTicks) / 1000.0;

    private bool IsPlaying => state is LauncherState.Playing;

    /// <summary>The mode to play a card in: the remembered one for the app, <c>--mode</c> or the best one for the command.</summary>
    private ModeChoice? ModeChoiceFor(DeviceInfo device) => launchMode switch
    {
        LaunchMode.Command => options.Mode,
        LaunchMode.App => settings.ModeChoices.TryGetValue(device.UniqueId, out var key) ? ModeChoice.FromKey(key) : null,
        _ => throw new ArgumentOutOfRangeException(nameof(device), launchMode, "Unhandled launch mode"),
    };

    private void Send(LauncherEvent launcherEvent)
    {
        var next = LauncherTransitions.Next(state, launcherEvent);
        state = next;
        var model = LauncherModel();
        var id = LauncherView.ScreenId(model);
        if (id != screenId)
        {
            // Crossfade from the screen that was showing (the video counts as one too).
            fadingScreen = screenId.Length > 0 && screenId != "playing" ? (previousModel ?? model, screenSince) : null;
            fadingSince = Now;
            screenId = id;
            screenSince = Now;
        }
        previousModel = model;
        UpdateTray();
        window.Invalidate();
        Accessibility.StructureChanged();
    }

    private LauncherModel? previousModel;

    private LauncherModel LauncherModel()
    {
        var isAutoPlayPending = state is LauncherState.Ready ready && ready.Devices.Count > 1 && DeviceSelection.ShouldAutoPlay(ready.Devices, settings.PreferredDeviceId);
        return new LauncherModel(
            state,
            Details,
            isStarting ? null : device =>
            {
                settings.PreferredDeviceId = device.UniqueId;
                settings.Save();
                _ = PlayAsync(device);
            },
            isAutoPlayPending,
            state is LauncherState.PermissionNeeded ? ContinueToPermissions : null,
            () => Permissions.OpenUri(LauncherView.HelpUrl),
            Permissions.OpenSettings);
    }

    private string Details(DeviceInfo device)
    {
        if (!modesByDevice.TryGetValue(device.UniqueId, out var modes)) return "";
        return VideoModes.ModeIndex(modes, ModeChoiceFor(device)) is { } index ? modes[index].Summary : "";
    }

    /// <summary>The packaged app's first run: the reasons were read; opening the card makes Windows ask.</summary>
    private void ContinueToPermissions()
    {
        settings.HasSeenPermissions = true;
        settings.Save();
        Send(new LauncherEvent.PermissionsGranted());
        _ = RefreshDevicesAsync();
    }

    // MARK: - Devices

    /// <summary>Reports the playable devices and, when nothing plays, starts the remembered or only one.</summary>
    private async Task RefreshDevicesAsync()
    {
        if (isRefreshing)
        {
            isRefreshAgain = true;
            return;
        }
        isRefreshing = true;
        try
        {
            do
            {
                isRefreshAgain = false;
                await RefreshOnceAsync();
            }
            while (isRefreshAgain);
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"plugview: cannot list devices: {error.Message}");
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private async Task RefreshOnceAsync()
    {
        var found = await DeviceCatalog.VideoDevicesAsync();
        var nowPlayable = DeviceSelection.PreferredFirst(DeviceSelection.PlayableVideoDevices(found, options.VideoQuery), settings.PreferredDeviceId);
        // Modes are read by opening each card briefly, off the window's thread; a playing card keeps what it had.
        foreach (var device in nowPlayable.Where(device => !modesByDevice.ContainsKey(device.UniqueId) && device != playback?.Device))
        {
            var modes = await Task.Run(() => DeviceCatalog.Modes(device));
            if (modes.Count > 0) modesByDevice[device.UniqueId] = modes;
        }
        var previous = playable;
        videoDevices = found;
        playable = nowPlayable;

        // The playing card went away (even mid-reopen, with nothing open): release it before the launcher shows what is left.
        if (state is LauncherState.Playing { Device: var shown } && !playable.Contains(shown))
        {
            isReopening = false;
            StopPlayback();
            StopSignalWatch();
            Console.WriteLine("Capture device disconnected. Waiting for it to come back…");
            Send(new LauncherEvent.PlaybackStopped());
        }
        Send(new LauncherEvent.DevicesChanged(playable));

        // A closed window (the app in the tray) plays nothing; a new card may open it.
        if (!window.IsVisible)
        {
            OpenOnPlugIn(previous, playable);
            return;
        }
        if (playback is null && !isStarting && state is LauncherState.Ready && playable.Count > 0
            && DeviceSelection.ShouldAutoPlay(playable, settings.PreferredDeviceId))
        {
            await PlayAsync(playable[0]);
        }
    }

    // MARK: - Playback

    /// <summary>Starts <paramref name="device"/>; a reopen by the signal watch fails quietly and keeps "No signal" up.</summary>
    private async Task PlayAsync(DeviceInfo device, bool isReopen = false)
    {
        if (isStarting) return;
        isStarting = true;
        window.Invalidate();
        try
        {
            StopPlayback();
            var audioInput = options.IsAudioEnabled ? await CardAudioAsync(device) : null;
            presenter.ExpandsLimitedRange = settings.ExpandsLimitedRange.GetValueOrDefault(device.UniqueId);
            var capture = new VideoCapture(device, ModeChoiceFor(device), presenter);
            var audio = StartAudio(audioInput, device);
            playback = new Playback(device, capture, audio);
            capture.Stopped += failure => uiThread.Post(() => OnCaptureStopped(capture, failure));
            capture.FrameArrived += OnFrame;
            ApplyVolume();
            capture.Start();
            audio?.Start();

            var audioSummary = audio is null ? "no audio" : $"audio from {audio.InputName}";
            Console.WriteLine($"Showing {device.Name} · {capture.Mode.Summary}, {audioSummary}");
            window.SetTitle($"{device.Name} · {capture.Mode.Summary}");
            if (!isReopen) FitWindowHeight(capture.Mode);
            Send(new LauncherEvent.PlaybackStarted(device));
            DidStartSignalWatch(device, audio is not null);
            if (!isReopen) ShowControls();
        }
        catch (Exception failure) when (failure is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            StopPlayback();
            Console.Error.WriteLine($"plugview: {CaptureErrors.Describe(failure, device.Name)}");
            if (isReopen) return;
            Send(IsAccessDenied(failure)
                ? new LauncherEvent.PermissionDenied(PermissionKind.Camera)
                : new LauncherEvent.PlaybackFailed(new CaptureFailure(device.Name, CaptureErrors.Describe(failure, device.Name))));
        }
        finally
        {
            isStarting = false;
            window.Invalidate();
        }
    }

    /// <summary>The card's own audio input, waiting up to <see cref="Tuning.CardAudioWaitSeconds"/> for it to appear.</summary>
    private async Task<DeviceInfo?> CardAudioAsync(DeviceInfo video)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Tuning.CardAudioWaitSeconds);
        do
        {
            var audio = DeviceSelection.SelectAudioDevice(await DeviceCatalog.AudioInputsAsync(videoDevices), video, options.AudioQuery);
            if (audio is not null) return audio;
            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);
        Console.Error.WriteLine($"plugview: no audio input matched {video.Name}; playing without sound.");
        return null;
    }

    private AudioPassthrough? StartAudio(DeviceInfo? audioInput, DeviceInfo video)
    {
        if (audioInput is null) return null;
        try
        {
            return new AudioPassthrough(audioInput.UniqueId, video.UniqueId);
        }
        catch (Exception failure) when (failure is COMException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"plugview: no sound: {CaptureErrors.Describe(failure, audioInput.Name)}");
            if (IsAccessDenied(failure)) ShowToast(Glyphs.Mute, "Sound is off: turn on Microphone access in Settings", isWarning: true, "Open", () => Permissions.OpenSettings(PermissionKind.Microphone));
            return null;
        }
    }

    /// <summary>The capture ended on its own: before any frame it could not start; later the signal watch reopens it.</summary>
    private void OnCaptureStopped(VideoCapture capture, Exception failure)
    {
        if (playback?.Capture != capture) return;
        Console.Error.WriteLine($"plugview: {CaptureErrors.Describe(failure, playback.Device.Name)}");
        var device = playback.Device;
        var hadFrame = HasHadFrame;
        StopPlayback();
        if (hadFrame)
        {
            // Unplugged cards are handled by the device watch; anything else gets reopened in place.
            _ = ReopenAsync(device);
            return;
        }
        Send(new LauncherEvent.PlaybackFailed(new CaptureFailure(device.Name, CaptureErrors.Describe(failure, device.Name))));
    }

    private void StopPlayback()
    {
        if (playback is null) return;
        StopSignalWatch();
        var stopping = playback;
        playback = null;
        stopping.Dispose();
        window.SetTitle("plugview");
    }

    /// <summary>Plays <paramref name="device"/> again from scratch (a new mode, Full-range colors, a reopen).</summary>
    private async Task RestartAsync(DeviceInfo device)
    {
        StopPlayback();
        await PlayAsync(device);
    }

    private static bool IsAccessDenied(Exception failure) => (uint)failure.HResult == 0x80070005 || failure is UnauthorizedAccessException;

    private void OnPowerChanged(bool isSuspending)
    {
        if (isSuspending)
        {
            // Release the card before sleep; Media Foundation sources do not survive it.
            if (playback is { Device: var device })
            {
                resumeDevice = device;
                StopPlayback();
            }
            return;
        }
        if (resumeDevice is { } resumed)
        {
            resumeDevice = null;
            _ = PlayAfterWakeAsync(resumed);
        }
    }

    private DeviceInfo? resumeDevice;

    private async Task PlayAfterWakeAsync(DeviceInfo device)
    {
        // USB devices come back a moment after the system does.
        await Task.Delay(TimeSpan.FromSeconds(2));
        await RefreshDevicesAsync();
        if (playback is null && playable.Contains(device)) await PlayAsync(device);
    }

    // MARK: - Window content

    public bool Draw(Canvas canvas, UiFrame frame, Interaction interaction)
    {
        var now = Now;
        var isAnimated = SystemAnimations.AreEnabled;
        if (!IsPlaying)
        {
            LauncherView.DrawBackground(canvas);
            var fade = isAnimated ? Math.Min(1, (now - fadingSince) / ScreenFadeSeconds) : 1;
            if (fadingScreen is { } fading && fade < 1)
            {
                canvas.PushOpacity((float)(1 - fade));
                LauncherView.Draw(canvas, frame, interaction, fading.Model, now - fading.Since, isAnimated, register: false);
                canvas.PopLayer();
            }
            canvas.PushOpacity((float)fade);
            LauncherView.Draw(canvas, frame, interaction, LauncherModel(), now - screenSince, isAnimated);
            canvas.PopLayer();
            DrawToasts(canvas, frame, interaction, now);
            return true;
        }
        return DrawPlaying(canvas, frame, interaction, now, isAnimated);
    }

    public bool IsAnimating
    {
        get
        {
            if (!SystemAnimations.AreEnabled) return HasMovingOverlay(Now);
            if (!IsPlaying)
            {
                var art = state switch
                {
                    LauncherState.WaitingForSignal => true,
                    LauncherState.Ready => true,
                    _ => false,
                };
                return art || Now - fadingSince < ScreenFadeSeconds || HasMovingOverlay(Now);
            }
            return HasMovingOverlay(Now);
        }
    }

    public float ChromeOpacity => IsPlaying ? ControlsOpacity(Now) : 1;

    public bool HidesPointer => IsPlaying && ControlsOpacity(Now) <= 0;

    public bool KeyDown(KeyPress key) => HandleKey(key);

    public void KeyUp(KeyPress key) => HandleKeyRelease(key);

    public void PointerMoved() => ShowControls();

    public void PointerLeft() => HideControlsSoon();

    public void Wheel(float notches, UiElement? over) => OnWheel(notches, over);

    public void Resized() => window.Invalidate();

    /// <summary>
    /// Releases the card at once (its light goes off with the window), remembers the window, and stays in the tray when
    /// the app has one; otherwise the window closes and plugview quits.
    /// </summary>
    public bool Closing()
    {
        if (window.RestoredFrame is { } frame && !window.IsMaximized) settings.WindowFrame = [frame.Left, frame.Top, frame.Width, frame.Height];
        settings.Save();
        if (playback is { } playing)
        {
            StopPlayback();
            Send(new LauncherEvent.PlaybackStopped());
            Send(new LauncherEvent.DevicesChanged(playable));
        }
        if (!ClosesToTray) return false;
        if (window.IsFullScreen) window.ToggleFullScreen();
        window.Hide();
        return true;
    }

    /// <summary>Opens the window from the tray, a second launch or a plugged-in card.</summary>
    public void OpenWindow()
    {
        window.Show();
        _ = RefreshDevicesAsync();
    }

    /// <summary>Quits for good: from the tray's Quit, or the last window closing without a tray.</summary>
    public void Quit()
    {
        isQuitting = true;
        Closing();
        window.Destroy();
    }

    private bool isQuitting;

    private bool ClosesToTray => !isQuitting && launchMode == LaunchMode.App && HasTray && settings.KeepsRunningInTray;

    /// <summary>Runs the window until plugview quits; the command's exit code.</summary>
    public static int Run()
    {
        AppWindow.RunMessageLoop();
        return 0;
    }

    public void Dispose()
    {
        StopPlayback();
        StopTray();
        deviceWatch.Dispose();
        Accessibility.Dispose();
        presenter.Dispose();
        window.Dispose();
    }
}
