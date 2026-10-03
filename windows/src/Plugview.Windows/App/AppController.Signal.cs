using Plugview.Core;
using Plugview.Core.Launcher;
using Plugview.Core.Selection;
using Plugview.Windows.Graphics;
using Plugview.Windows.Ui;
using Plugview.Windows.Video;

namespace Plugview.Windows.App;

/// <summary>
/// The signal watch (<c>spec/README.md</c>, "Signal watch and picture help"), as the Mac's <c>SignalMonitor</c> and the
/// Linux <c>signal_watch.rs</c>.
/// </summary>
/// <remarks>
/// Twice a second it reads when the last frame arrived, how bright a small sampled frame is, and the card audio's
/// level. Frames that stop while the card is connected show "No signal" and reopen the card in place with the shared
/// backoff; a black picture or silence since the start shows the console checklist once. Sampling runs after the frame
/// is presented, so it never delays the picture.
/// </remarks>
internal sealed partial class AppController
{
    private static readonly TimeSpan SignalTick = TimeSpan.FromMilliseconds(500);
    private const float SoundLevelDecibels = -60;

    private static readonly string[] PictureHelp =
    [
        "PlayStation 5: Settings › System › HDMI › turn off Enable HDCP. PlayStation 4: Settings › System › turn off Enable HDCP.",
        "Set the console to 1080p, and turn off 120 Hz and VRR output.",
        "Nintendo Switch: it must sit in its dock. Switch Lite has no video output.",
        "Unplug the HDMI cable from the card and plug it back in.",
    ];

    private static readonly string[] SoundHelp =
    [
        "PlayStation 5: Settings › Sound › Audio Output › Audio Format (Priority) › Linear PCM.",
        "Xbox: Settings › General › Volume & audio output › HDMI audio › Stereo uncompressed.",
        "Nintendo Switch 2: set TV sound to Stereo.",
        "Unplug headphones from the controller or the console: game sound then goes back to HDMI.",
    ];

    private int signalGeneration;
    private DeviceInfo? signalDevice;
    private bool signalHasAudio;
    private bool isReopening;
    private int reopenAttempts;
    private double signalStartedAt;
    private double lastFrameAt;
    private long lastFrameTicks;
    private bool hasHadFrame;
    private bool hasReadLuma;
    private bool hasSeenPicture;
    private bool hasHeardSound;
    private readonly HashSet<SignalHint> shownHints = [];
    private SignalNotice? notice;

    /// <summary>What the signal watch shows over the video.</summary>
    private abstract record SignalNotice
    {
        /// <summary>Frames stopped while the card is connected; plugview is reopening it. Help after a few tries.</summary>
        public sealed record NoSignal(string DeviceName, bool ShowsHelp) : SignalNotice;

        /// <summary>Black frames since the start: usually HDCP.</summary>
        public sealed record BlackPicture : SignalNotice;

        /// <summary>Digital silence since the start, with a picture.</summary>
        public sealed record NoGameSound : SignalNotice;
    }

    /// <summary>A frame arrived (capture thread): only its time is kept, read by the watch's next tick.</summary>
    private void OnFrame(FrameTiming timing) => Interlocked.Exchange(ref lastFrameTicks, Environment.TickCount64);

    /// <summary>A frame arrived since the start: a capture error is then a lost signal, not a card that cannot start.</summary>
    private bool HasHadFrame => hasHadFrame || Interlocked.Read(ref lastFrameTicks) > 0;

    /// <summary>Call after every successful start. A start the watch asked for keeps its tries; any other starts afresh.</summary>
    private void DidStartSignalWatch(DeviceInfo device, bool hasAudio)
    {
        signalHasAudio = hasAudio;
        Interlocked.Exchange(ref lastFrameTicks, 0);
        if (isReopening)
        {
            isReopening = false;
        }
        else
        {
            signalDevice = device;
            reopenAttempts = 0;
            hasHadFrame = false;
            hasReadLuma = false;
            hasSeenPicture = false;
            hasHeardSound = false;
            shownHints.Clear();
            SetNotice(null);
            signalStartedAt = Now;
            lastFrameAt = Now;
        }
        _ = WatchSignalAsync(++signalGeneration);
    }

    /// <summary>Call when nothing plays any more (unplugged, window closed, failed).</summary>
    private void StopSignalWatch()
    {
        signalGeneration++;
        if (!isReopening) SetNotice(null);
    }

    private async Task WatchSignalAsync(int generation)
    {
        while (generation == signalGeneration)
        {
            await Task.Delay(SignalTick);
            if (generation != signalGeneration || !IsPlaying) return;
            TickSignalWatch();
        }
    }

    private void TickSignalWatch()
    {
        var now = Now;
        var frameTicks = Interlocked.Read(ref lastFrameTicks);
        if (frameTicks > 0)
        {
            var frameAt = (frameTicks - startTicks) / 1000.0;
            if (frameAt > lastFrameAt) NoteFrame(frameAt);
        }
        if (playback?.Audio is { } audio && audio.LevelDecibels > SoundLevelDecibels) hasHeardSound = true;

        var watch = SignalWatcher.Watch(now - lastFrameAt, reopenAttempts);
        if (watch.ShowsNoSignal)
        {
            SetNotice(new SignalNotice.NoSignal(signalDevice?.Name ?? "The card", watch.ShowsSignalHelp));
            if (watch.ShouldReopen && signalDevice is { } device)
            {
                reopenAttempts++;
                isReopening = true;
                Console.WriteLine($"No frames from {device.Name}. Reconnecting…");
                _ = ReopenAsync(device);
            }
            return;
        }

        // A small sample of the frame on screen, read back after it is presented.
        presenter.RequestGrab(64, 36, grab =>
        {
            if (grab is null) return;
            var luma = grab.MaxLuma();
            uiThread.Post(() => NoteLuma(luma));
        });

        var sinceStart = now - signalStartedAt;
        var blackSeconds = hasReadLuma && !hasSeenPicture ? sinceStart : 0;
        double? silentSeconds = signalHasAudio && !hasHeardSound ? sinceStart : null;
        foreach (var hint in SignalWatcher.Hints(blackSeconds, silentSeconds).Where(shownHints.Add)) ShowHint(hint);
    }

    private void NoteFrame(double frameAt)
    {
        lastFrameAt = frameAt;
        hasHadFrame = true;
        reopenAttempts = 0;
        if (notice is SignalNotice.NoSignal) SetNotice(null);
    }

    private void NoteLuma(int luma)
    {
        hasReadLuma = true;
        if (SignalWatcher.IsBlackFrame(luma)) return;
        hasSeenPicture = true;
        if (notice is SignalNotice.BlackPicture) SetNotice(null);
    }

    /// <summary>
    /// Closes and reopens the card in place. A failed start keeps "No signal" up; the next backoff tries again, and a
    /// card that went away is handled by the device watch.
    /// </summary>
    private async Task ReopenAsync(DeviceInfo device)
    {
        isReopening = true;
        StopPlayback();
        presenter.Clear();
        await PlayAsync(device, isReopen: true);
        if (playback is null)
        {
            // Still "Playing" for the launcher: the notice covers the window and the watch keeps trying, with backoff.
            isReopening = false;
            _ = WatchSignalAsync(++signalGeneration);
        }
    }

    private void ShowHint(SignalHint hint)
    {
        switch (hint)
        {
            case SignalHint.BlackPicture:
                SetNotice(new SignalNotice.BlackPicture());
                break;
            case SignalHint.NoGameSound:
                // The picture is fine, so a toast rather than a panel over it; the panel is one click away.
                ShowToast(Glyphs.Mute, "No game sound", isWarning: true, "Help", () => SetNotice(new SignalNotice.NoGameSound()));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hint), hint, "Unhandled signal hint");
        }
    }

    private void SetNotice(SignalNotice? next)
    {
        if (notice == next) return;
        notice = next;
        if (next is SignalNotice.NoSignal) presenter.Clear();
        window.Invalidate();
        Accessibility.StructureChanged();
        if (next is not null) Accessibility.Announce(NoticeTitle(next));
    }

    private static string NoticeTitle(SignalNotice shown) => shown switch
    {
        SignalNotice.NoSignal => "NO SIGNAL",
        SignalNotice.BlackPicture => "BLACK PICTURE",
        SignalNotice.NoGameSound => "NO GAME SOUND",
        _ => throw new ArgumentOutOfRangeException(nameof(shown), shown, "Unhandled notice"),
    };

    /// <summary>"No signal" covers the picture (never a frozen frame); the hints are a panel with a Close button.</summary>
    private bool DrawNotice(Canvas canvas, UiFrame frame, Interaction interaction)
    {
        if (notice is not { } shown) return false;
        var (message, checklist, closes) = shown switch
        {
            SignalNotice.NoSignal noSignal => ($"{noSignal.DeviceName} stopped sending a picture. Reconnecting…", noSignal.ShowsHelp ? PictureHelp : [], false),
            SignalNotice.BlackPicture => ("The card sends a black picture. Usually the console's HDCP protection:", PictureHelp, true),
            SignalNotice.NoGameSound => ("The card sends silence. The console may be sending surround or Dolby sound:", SoundHelp, true),
            _ => throw new InvalidOperationException($"Unhandled notice {shown}"),
        };
        if (shown is SignalNotice.NoSignal) LauncherView.DrawBackground(canvas);

        const float padding = 22;
        var width = Math.Min(560, canvas.Width - 48);
        var textWidth = width - (2 * padding);
        var messageStyle = new TextStyle(Typeface.Sans, 15);
        var lineStyle = new TextStyle(Typeface.Sans, 13);
        var messageHeight = canvas.Measure(message, messageStyle, textWidth).Height;
        var lineHeights = checklist.Select(line => canvas.Measure(line, lineStyle, textWidth - 16).Height).ToArray();
        var height = padding + 20 + 12 + messageHeight + lineHeights.Sum(lineHeight => lineHeight + 8) + (closes ? 12 + 40 : 0) + padding;
        var panel = new Box((canvas.Width - width) / 2, (canvas.Height - height) / 2, width, height);
        canvas.FillRoundedRectangle(panel, 14, Palette.Cathode.WithAlpha(0.88f));
        canvas.StrokeRoundedRectangle(panel, 14, 1, Palette.Line);

        var top = panel.Y + padding;
        var titleBox = new Box(panel.X + padding, top, textWidth, 20);
        canvas.Text(NoticeTitle(shown), new TextStyle(Typeface.Mono, 13, 600), titleBox, Palette.Signal, TextAlignment.Leading);
        frame.Add(new UiElement("notice-title", titleBox, UiRole.Text, NoticeTitle(shown)));
        top += 20 + 12;
        var messageBox = new Box(panel.X + padding, top, textWidth, messageHeight);
        canvas.Text(message, messageStyle, messageBox, Palette.Bone, TextAlignment.Leading);
        frame.Add(new UiElement("notice-message", messageBox, UiRole.Text, message));
        top += messageHeight;
        for (var index = 0; index < checklist.Length; index++)
        {
            top += 8;
            canvas.FillEllipse(panel.X + padding + 3, top + 9, 2.5f, Palette.Signal);
            var lineBox = new Box(panel.X + padding + 16, top, textWidth - 16, lineHeights[index]);
            canvas.Text(checklist[index], lineStyle, lineBox, Palette.Muted, TextAlignment.Leading);
            frame.Add(new UiElement($"notice-line-{index}", lineBox, UiRole.Text, checklist[index]));
            top += lineHeights[index];
        }
        if (closes)
        {
            var (closeWidth, _) = canvas.Measure("Close", new TextStyle(Typeface.Mono, 13, 500));
            Widgets.BrandButton(canvas, frame, interaction, "notice-close", "Close", isPrimary: false, panel.Right - padding - ((closeWidth + 36) / 2), top + 12, () => SetNotice(null));
        }
        return true;
    }
}
