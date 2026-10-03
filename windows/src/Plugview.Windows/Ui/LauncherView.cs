using Plugview.Core.Brand;
using Plugview.Core.Launcher;
using Plugview.Core.Selection;
using Plugview.Windows.Graphics;

namespace Plugview.Windows.Ui;

/// <summary>What a launcher screen needs besides the state.</summary>
/// <param name="State">The launcher state.</param>
/// <param name="Details">For a found card: "1920×1080 · 60 fps", or empty.</param>
/// <param name="Play">Plays a card; <c>null</c> while one is starting.</param>
/// <param name="IsAutoPlayPending">Several cards, one remembered: it plays unless the user picks another.</param>
/// <param name="ContinueToPermissions">First run of the packaged app: shows the reasons, then lets Windows ask.</param>
/// <param name="OpenHelp">Opens the troubleshooting guide.</param>
/// <param name="OpenPrivacySettings">Opens Windows' privacy page for a permission.</param>
internal sealed record LauncherModel(
    LauncherState State,
    Func<DeviceInfo, string> Details,
    Action<DeviceInfo>? Play,
    bool IsAutoPlayPending,
    Action? ContinueToPermissions,
    Action OpenHelp,
    Action<PermissionKind> OpenPrivacySettings);

/// <summary>
/// Everything the window shows while no video is on screen: one layout per <see cref="LauncherState"/>, as on the Mac
/// (artwork, a mono amber title, a message, then actions) on the glowing CRT surface.
/// </summary>
internal static class LauncherView
{
    public const string HelpUrl = "https://plugview.app/guide/#troubleshooting";

    private const float ColumnWidth = 440;
    private static readonly TextStyle TitleText = new(Typeface.Mono, 14);
    private static readonly TextStyle MessageText = new(Typeface.Sans, 15);

    /// <summary>A stable name per screen: the window crossfades between screens but not within one.</summary>
    public static string ScreenId(LauncherModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.State switch
        {
            LauncherState.PermissionNeeded => model.ContinueToPermissions is null ? "permission-prompt" : "onboarding",
            LauncherState.PermissionDenied denied => $"denied-{denied.Kind}",
            LauncherState.WaitingForSignal => "waiting",
            LauncherState.Ready => "ready",
            LauncherState.Playing => "playing",
            LauncherState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(model), model.State, "Unhandled launcher state"),
        };
    }

    /// <summary>The CRT surface behind every screen: a warm glow above the middle fading to the surface color.</summary>
    public static void DrawBackground(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.FillRadialGradient(canvas.Bounds, canvas.Width / 2, canvas.Height * 0.4f, 700, Palette.Glow, Palette.Surface);
    }

    /// <summary>
    /// Draws the screen for <paramref name="model"/>, its artwork <paramref name="seconds"/> into its animation (resting
    /// when <paramref name="isAnimated"/> is false). With <paramref name="register"/> false (a screen fading out) it is
    /// drawn but no longer clickable.
    /// </summary>
    public static void Draw(Canvas canvas, UiFrame frame, Interaction interaction, LauncherModel model, double seconds, bool isAnimated, bool register = true)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(model);
        var target = register ? frame : new UiFrame();
        var screen = Screen(model);
        if (screen is null) return;
        Layout(canvas, target, interaction, screen, seconds, isAnimated);
    }

    private sealed record ScreenContent(Art Art, string Title, bool IsError, string? Message, IReadOnlyList<Row> Actions);

    /// <summary>One row of actions: its height for layout, then how to draw it at a top edge.</summary>
    private sealed record Row(float Height, Action<Canvas, UiFrame, Interaction, float> Draw);

    private static ScreenContent? Screen(LauncherModel model) => model.State switch
    {
        LauncherState.PermissionNeeded needed when model.ContinueToPermissions is { } continueToPermissions => Onboarding(continueToPermissions),
        LauncherState.PermissionNeeded needed => new(Art.Mark, "WAITING FOR PERMISSION", false, $"Allow {Name(needed.Kind)} access in the Windows prompt.", []),
        LauncherState.PermissionDenied denied => Denied(denied.Kind, model.OpenPrivacySettings),
        LauncherState.WaitingForSignal => new(Art.Waiting, "WAITING FOR SIGNAL", false, "Plug a capture card into a USB port.\nYour console will show up here.", [HelpButton(model.OpenHelp)]),
        LauncherState.Ready ready => Ready(ready.Devices, model),
        LauncherState.Playing => null,
        LauncherState.Failed failed => new(Art.Mark, "COULD NOT START", true, $"{failed.Failure.DeviceName}: {failed.Failure.Reason}", [HelpButton(model.OpenHelp)]),
        _ => throw new ArgumentOutOfRangeException(nameof(model), model.State, "Unhandled launcher state"),
    };

    private static ScreenContent Ready(IReadOnlyList<DeviceInfo> devices, LauncherModel model)
    {
        var isPicking = devices.Count > 1 && model.Play is not null;
        var message = (isPicking, model.IsAutoPlayPending) switch
        {
            (true, true) => "Playing the one you used last, or pick another.",
            (true, false) => "Pick one. plugview remembers your choice.",
            _ => null,
        };
        var rows = new List<Row>();
        foreach (var device in devices)
        {
            var play = isPicking && model.Play is { } pick ? () => pick(device) : (Action?)null;
            var details = model.Details(device);
            rows.Add(new Row(62, (canvas, frame, interaction, top) =>
                Widgets.DeviceRow(canvas, frame, interaction, $"device:{device.UniqueId}", device.Name, details, canvas.Width / 2, top, play)));
        }
        if (model.Play is { } playFirst && devices.Count > 0 && (devices.Count == 1 || model.IsAutoPlayPending))
        {
            var first = devices[0];
            var label = devices.Count == 1 ? "Play" : $"Play {first.Name}";
            rows.Add(new Row(40, (canvas, frame, interaction, top) =>
                Widgets.BrandButton(canvas, frame, interaction, "play", label, isPrimary: true, canvas.Width / 2, top, () => playFirst(first), Glyphs.Play)));
        }
        return new(Art.Signal, "SIGNAL FOUND", false, message, rows);
    }

    /// <summary>First run of the packaged app: says why each permission is needed before Windows asks.</summary>
    private static ScreenContent Onboarding(Action continueToPermissions)
    {
        var reasons = new Row(150, (canvas, frame, interaction, top) =>
        {
            var box = new Box((canvas.Width / 2) - 200, top, 400, 150);
            canvas.FillRoundedRectangle(box, 10, Palette.Raised);
            canvas.StrokeRoundedRectangle(box, 10, 1, Palette.Line);
            Reason(canvas, frame, Glyphs.Video, "Camera", "Shows the picture from your capture card. plugview never turns on your PC's camera.", box.X + 16, box.Y + 16);
            Reason(canvas, frame, Glyphs.Speaker, "Microphone", "Plays the sound from your console. plugview never listens to your PC's microphone.", box.X + 16, box.Y + 82);
        });
        var button = new Row(40, (canvas, frame, interaction, top) =>
            Widgets.BrandButton(canvas, frame, interaction, "continue", "Continue", isPrimary: true, canvas.Width / 2, top, continueToPermissions));
        return new(Art.Mark, "TWO PERMISSIONS", false, "Windows asks for each one when you continue.", [reasons, button]);
    }

    private static void Reason(Canvas canvas, UiFrame frame, char glyph, string title, string detail, float x, float y)
    {
        canvas.Icon(glyph, 16, new Box(x, y, 22, 20), Palette.Signal);
        canvas.Text(title, new TextStyle(Typeface.Sans, 15, 600), new Box(x + 34, y, 330, 20), Palette.Bone, TextAlignment.Leading);
        canvas.Text(detail, new TextStyle(Typeface.Sans, 13), new Box(x + 34, y + 22, 330, 36), Palette.Muted, TextAlignment.Leading);
        frame.Add(new UiElement($"reason:{title}", new Box(x, y, 368, 58), UiRole.Text, $"{title}: {detail}"));
    }

    /// <summary>A refused permission plugview cannot play without, with a way to turn it back on.</summary>
    private static ScreenContent Denied(PermissionKind kind, Action<PermissionKind> openSettings)
    {
        var setting = kind switch
        {
            PermissionKind.Camera => "Camera",
            PermissionKind.Microphone => "Microphone",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled permission"),
        };
        var button = new Row(40, (canvas, frame, interaction, top) =>
            Widgets.BrandButton(canvas, frame, interaction, "open-settings", "Open Settings", isPrimary: true, canvas.Width / 2, top, () => openSettings(kind)));
        return new(
            Art.Mark,
            $"{Name(kind).ToUpperInvariant()} ACCESS IS OFF",
            true,
            $"Turn on {setting} access and \"Let desktop apps access your {Name(kind)}\" in Settings › Privacy & security › {setting}, then reopen plugview.",
            [button]);
    }

    private static Row HelpButton(Action openHelp) =>
        new(40, (canvas, frame, interaction, top) => Widgets.BrandButton(canvas, frame, interaction, "help", "Help me connect", isPrimary: false, canvas.Width / 2, top, openHelp));

    private static string Name(PermissionKind kind) => kind switch
    {
        PermissionKind.Camera => "camera",
        PermissionKind.Microphone => "microphone",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled permission"),
    };

    /// <summary>The launcher's one layout: artwork, title, message, actions, centered on the window.</summary>
    private static void Layout(Canvas canvas, UiFrame frame, Interaction interaction, ScreenContent screen, double seconds, bool isAnimated)
    {
        var (artWidth, artHeight) = screen.Art switch
        {
            Art.Mark => (96f, 96f),
            Art.Waiting => (300f, 150f),
            Art.Signal => (150f, 150f),
            _ => throw new ArgumentOutOfRangeException(nameof(screen), screen.Art, "Unhandled artwork"),
        };
        var columnWidth = Math.Min(ColumnWidth, canvas.Width - 64);
        var messageHeight = screen.Message is { } message ? canvas.Measure(message, MessageText, columnWidth).Height + 8 : 0;
        var actionsHeight = screen.Actions.Count == 0 ? 0 : 18 + screen.Actions.Sum(row => row.Height) + (14 * (screen.Actions.Count - 1));
        const float titleHeight = 20;
        var total = artHeight + 20 + titleHeight + messageHeight + actionsHeight;
        var top = Math.Max(32, (canvas.Height - total) / 2);
        var centerX = canvas.Width / 2;

        canvas.Art(ArtScenes.Frame(screen.Art, seconds, isAnimated), new Box(centerX - (artWidth / 2), top, artWidth, artHeight));
        top += artHeight + 20;

        var titleColor = screen.IsError ? Palette.Tally : Palette.Signal;
        var titleBox = new Box(centerX - (columnWidth / 2), top, columnWidth, titleHeight);
        // The Mac's soft glow under the title, approximated with a faint wider copy.
        canvas.TrackedText(screen.Title, TitleText, titleBox with { Y = titleBox.Y + 1 }, titleColor.WithAlpha(0.18f), 2.8f);
        canvas.TrackedText(screen.Title, TitleText, titleBox, titleColor, 2.8f);
        frame.Add(new UiElement("title", titleBox, UiRole.Text, screen.Title));
        top += titleHeight;

        if (screen.Message is { } text)
        {
            var messageBox = new Box(centerX - (columnWidth / 2), top + 8, columnWidth, messageHeight - 8);
            canvas.Text(text, MessageText, messageBox, Palette.Muted);
            frame.Add(new UiElement("message", messageBox, UiRole.Text, text.Replace('\n', ' ')));
            top += messageHeight;
        }

        if (screen.Actions.Count == 0) return;
        top += 18;
        foreach (var row in screen.Actions)
        {
            row.Draw(canvas, frame, interaction, top);
            top += row.Height + 14;
        }
    }
}
