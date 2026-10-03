using Plugview.Core;
using Plugview.Core.Selection;
using Plugview.Windows.Capture;
using Plugview.Windows.Graphics;
using Plugview.Windows.Ui;
using Plugview.Windows.Video;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Plugview.Windows.App;

/// <summary>While a card plays: the controls and their fade, volume, screenshots, mode and device menus, window size, keys.</summary>
internal sealed partial class AppController
{
    /// <summary>Ctrl+= and Ctrl+- grow and shrink the window by this much, as ⌘+ and ⌘- on the Mac.</summary>
    private const float ZoomStep = 1.25f;
    private const double FadeSeconds = 0.22;
    private const float BarHeight = 48, BarBottomMargin = 22;
    private static readonly TextStyle BarText = new(Typeface.Mono, 12, 500);

    private bool areControlsShown;
    private double controlsFadeStart = double.NegativeInfinity;
    private int controlsGeneration;
    private bool isBarHovered;
    private bool isMenuOpen;
    private Toast? toast;

    /// <summary>A message at the top of the video that goes away on its own: longer with an action (as on the Mac).</summary>
    private sealed record Toast(char Glyph, string Message, bool IsWarning, string? ActionTitle, Action? Action, double ShownAt, double Seconds);

    // MARK: - Controls fade

    /// <summary>0 hidden … 1 shown, following the fade.</summary>
    private float ControlsOpacity(double now)
    {
        var progress = SystemAnimations.AreEnabled ? Math.Clamp((now - controlsFadeStart) / FadeSeconds, 0, 1) : 1;
        return (float)(areControlsShown ? progress : 1 - progress);
    }

    /// <summary>A fade or a toast on screen: the window redraws until it ends (the toast slides out on its own).</summary>
    private bool HasMovingOverlay(double now) =>
        now - controlsFadeStart < FadeSeconds || (toast is { } shown && now - shown.ShownAt < shown.Seconds + 0.1);

    /// <summary>Shows the controls and window buttons; they fade after <see cref="Tuning.ControlsHideSeconds"/> unless in use.</summary>
    private void ShowControls()
    {
        if (!IsPlaying) return;
        if (!areControlsShown)
        {
            areControlsShown = true;
            controlsFadeStart = Now - (FadeSeconds * (1 - ControlsOpacity(Now)));
            window.Invalidate();
        }
        _ = HideControlsAfterAsync(++controlsGeneration);
    }

    private async Task HideControlsAfterAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(Tuning.ControlsHideSeconds));
        if (generation != controlsGeneration) return;
        // The bar stays while the pointer rests on it, a menu is open, or the keyboard is on a control.
        if (isBarHovered || isMenuOpen || window.Interaction.ShowsFocus)
        {
            ShowControls();
            return;
        }
        SetControlsHidden();
    }

    /// <summary>The pointer left the window: the controls go at once (unless a menu is open).</summary>
    private void HideControlsSoon()
    {
        isBarHovered = false;
        if (IsPlaying && !isMenuOpen) SetControlsHidden();
    }

    private void SetControlsHidden()
    {
        if (!areControlsShown) return;
        areControlsShown = false;
        controlsFadeStart = Now - (FadeSeconds * ControlsOpacity(Now));
        window.Invalidate();
    }

    // MARK: - Drawing over the video

    private bool DrawPlaying(Canvas canvas, UiFrame frame, Interaction interaction, double now, bool isAnimated)
    {
        var drew = DrawNotice(canvas, frame, interaction);
        var opacity = ControlsOpacity(now);
        if (opacity > 0)
        {
            canvas.PushOpacity(opacity);
            DrawControlsBar(canvas, frame, interaction);
            canvas.PopLayer();
            drew = true;
        }
        else
        {
            isBarHovered = false;
        }
        return DrawToasts(canvas, frame, interaction, now) || drew;
    }

    /// <summary>
    /// The floating bar, as on the Mac: mute, volume, screenshot, resolution, device (with several cards), full screen.
    /// Every control is at least 32 DIPs tall, so it is easy to hit.
    /// </summary>
    private void DrawControlsBar(Canvas canvas, UiFrame frame, Interaction interaction)
    {
        if (playback is not { } playing) return;
        var modeLabel = ModeLabel(playing);
        var (modeTextWidth, _) = canvas.Measure(modeLabel, BarText);
        var hasDeviceMenu = playable.Count > 1;
        const float iconWidth = 36, sliderWidth = 112, percentWidth = 44, gap = 4, padding = 8;
        var modeWidth = modeTextWidth + 34;
        var width = (padding * 2) + iconWidth + sliderWidth + percentWidth + iconWidth + modeWidth + (hasDeviceMenu ? iconWidth : 0) + iconWidth + (gap * (hasDeviceMenu ? 6 : 5));
        var bar = new Box((canvas.Width - width) / 2, canvas.Height - BarBottomMargin - BarHeight, width, BarHeight);
        isBarHovered = bar.Contains(MouseX, MouseY);
        canvas.FillRoundedRectangle(bar, 14, Palette.Cathode.WithAlpha(0.78f));
        canvas.StrokeRoundedRectangle(bar, 14, 1, Palette.Line);
        // The bar itself takes clicks (they do nothing) so it never drags the window.
        frame.Add(new UiElement("controls", bar, UiRole.Text, "Controls", Invoke: () => { }));

        var x = bar.X + padding;
        var top = bar.Y + ((BarHeight - 32) / 2);
        var effective = isMuted ? 0 : volumePercent;
        var volumeGlyph = effective == 0 ? Glyphs.Mute : Glyphs.Volume;
        IconButton(canvas, frame, interaction, "mute", effective == 0 ? "Unmute (M)" : "Mute (M)", volumeGlyph, new Box(x, top, iconWidth, 32), ToggleMute);
        x += iconWidth + gap;

        var slider = new Box(x, top, sliderWidth, 32);
        var track = new Box(slider.X + 6, slider.Y + 14, slider.Width - 12, 4);
        canvas.FillRoundedRectangle(track, 2, Palette.Line);
        canvas.FillRoundedRectangle(track with { Width = track.Width * effective / 100f }, 2, Palette.Bone);
        canvas.FillEllipse(track.X + (track.Width * effective / 100f), track.Y + 2, interaction.IsHovered("volume") || interaction.IsPressed("volume") ? 7 : 6, Palette.Bone);
        Widgets.FocusRing(canvas, interaction, "volume", slider, 6);
        frame.Add(new UiElement("volume", slider, UiRole.Slider, "Volume", Value: effective, Drag: pointerX =>
            SetVolume((int)Math.Round(Math.Clamp((pointerX - track.X) / track.Width, 0, 1) * 100))));
        x += sliderWidth + gap;
        canvas.Text($"{effective}%", BarText, new Box(x, top, percentWidth - 6, 32), Palette.Bone, TextAlignment.Trailing);
        x += percentWidth + gap;

        IconButton(canvas, frame, interaction, "screenshot", "Screenshot (S)", Glyphs.Camera, new Box(x, top, iconWidth, 32), TakeScreenshot);
        x += iconWidth + gap;

        var modeBox = new Box(x, top, modeWidth, 32);
        if (interaction.IsHovered("mode")) canvas.FillRoundedRectangle(modeBox, 8, new Rgba(1, 1, 1, 0.08f));
        canvas.Text(modeLabel, BarText, modeBox with { Width = modeBox.Width - 16 }, Palette.Bone);
        canvas.Icon(Glyphs.ChevronDown, 9, new Box(modeBox.Right - 22, modeBox.Y, 16, 32), Palette.Muted);
        Widgets.FocusRing(canvas, interaction, "mode", modeBox, 8);
        frame.Add(new UiElement("mode", modeBox, UiRole.MenuButton, $"Resolution and frame rate: {modeLabel}", () => ShowModeMenu(modeBox)));
        x += modeWidth + gap;

        if (hasDeviceMenu)
        {
            var deviceBox = new Box(x, top, iconWidth, 32);
            IconButton(canvas, frame, interaction, "device", "Capture device", Glyphs.Devices, deviceBox, () => ShowDeviceMenu(deviceBox));
            x += iconWidth + gap;
        }
        IconButton(canvas, frame, interaction, "full-screen", window.IsFullScreen ? "Exit Full Screen (F)" : "Full Screen (F)", window.IsFullScreen ? Glyphs.BackToWindow : Glyphs.FullScreen, new Box(x, top, iconWidth, 32), ToggleFullScreen);
    }

    private static void IconButton(Canvas canvas, UiFrame frame, Interaction interaction, string id, string name, char glyph, Box box, Action invoke)
    {
        if (interaction.IsHovered(id)) canvas.FillRoundedRectangle(box, 8, new Rgba(1, 1, 1, interaction.IsPressed(id) ? 0.14f : 0.08f));
        canvas.Icon(glyph, 15, box, Palette.Bone);
        Widgets.FocusRing(canvas, interaction, id, box, 8);
        // Narrator reads the name without the key in parentheses.
        frame.Add(new UiElement(id, box, UiRole.Button, name.Split(" (")[0], invoke));
    }

    /// <summary>The resolution button's text: "Auto" or the chosen mode, e.g. "1080p60".</summary>
    private string ModeLabel(Playback playing) =>
        ModeChoiceFor(playing.Device) is { } choice ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{choice.Height}p{choice.FrameRate}") : "Auto";

    private bool DrawToasts(Canvas canvas, UiFrame frame, Interaction interaction, double now)
    {
        if (toast is not { } shown) return false;
        var age = now - shown.ShownAt;
        if (age >= shown.Seconds)
        {
            toast = null;
            return false;
        }
        // Slides down from the top edge, then back up when its time is over.
        var slide = (float)Math.Min(Math.Min(age, shown.Seconds - age) / 0.3, 1);
        if (!SystemAnimations.AreEnabled) slide = 1;
        var messageStyle = new TextStyle(Typeface.Sans, 14, 500);
        var (textWidth, _) = canvas.Measure(shown.Message, messageStyle);
        var actionWidth = shown.ActionTitle is { } title ? canvas.Measure(title, new TextStyle(Typeface.Mono, 13, 500)).Width + 36 + 10 : 0;
        var width = Math.Min(canvas.Width - 32, 16 + 20 + 10 + textWidth + actionWidth + 16);
        var box = new Box((canvas.Width - width) / 2, -50 + (64 * slide), width, 46);
        canvas.PushOpacity(slide);
        canvas.FillRoundedRectangle(box, 12, Palette.Cathode.WithAlpha(0.85f));
        canvas.StrokeRoundedRectangle(box, 12, 1, Palette.Line);
        canvas.Icon(shown.Glyph, 15, new Box(box.X + 14, box.Y, 22, box.Height), shown.IsWarning ? Palette.Signal : Palette.Ok);
        canvas.Text(shown.Message, messageStyle, new Box(box.X + 46, box.Y, textWidth + 4, box.Height), Palette.Bone, TextAlignment.Leading);
        frame.Add(new UiElement("toast", box, UiRole.Text, shown.Message));
        if (shown.ActionTitle is { } action && shown.Action is { } run)
        {
            Widgets.BrandButton(canvas, frame, interaction, "toast-action", action, isPrimary: false, box.Right - 16 - ((actionWidth - 10) / 2), box.Y + 3, () =>
            {
                toast = null;
                run();
            });
        }
        canvas.PopLayer();
        return true;
    }

    private void ShowToast(char glyph, string message, bool isWarning, string? actionTitle = null, Action? action = null)
    {
        toast = new Toast(glyph, message, isWarning, actionTitle, action, Now, action is null ? 3 : 6);
        window.Invalidate();
        Accessibility.Announce(message);
    }

    // MARK: - Volume

    private void SetVolume(int percent)
    {
        volumePercent = Math.Clamp(percent, 0, 100);
        isMuted = false;
        ApplyVolume();
    }

    private void ToggleMute()
    {
        isMuted = !isMuted;
        ApplyVolume();
        Console.WriteLine(isMuted ? "Muted" : $"Volume {volumePercent}%");
    }

    private void ApplyVolume()
    {
        if (playback?.Audio is { } audio) audio.Volume = isMuted ? 0 : volumePercent / 100f;
        window.Invalidate();
    }

    /// <summary>The wheel over the controls changes the volume; up (away from the user) is always louder.</summary>
    private void OnWheel(float notches, UiElement? over)
    {
        if (!IsPlaying || over is null) return;
        SetVolume((isMuted ? 0 : volumePercent) + (int)Math.Round(notches * 2));
        ShowControls();
    }

    // MARK: - Screenshot

    /// <summary>Saves the picture on screen to Pictures\plugview and says where.</summary>
    private void TakeScreenshot()
    {
        if (playback is null) return;
        presenter.RequestGrab(0, 0, grab => uiThread.Post(() => SaveScreenshot(grab)));
    }

    private void SaveScreenshot(FrameGrab? grab)
    {
        try
        {
            if (grab is null) throw new IOException("no frame to save");
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "plugview");
            Directory.CreateDirectory(folder);
            var now = DateTime.Now;
            var path = Path.Combine(folder, Screenshots.FileName(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second));
            using (var file = File.Create(path)) PngWriter.Write(file, grab.Width, grab.Height, grab.Bgra);
            Console.WriteLine($"Screenshot saved to {path}");
            ShowToast(Glyphs.Camera, "Screenshot saved", isWarning: false, "Show", () => Permissions.OpenUri("explorer.exe", $"/select,\"{path}\""));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"plugview: screenshot failed: {error.Message}");
            ShowToast(Glyphs.Warning, "Could not save the screenshot", isWarning: true);
        }
    }

    // MARK: - Menus: mode, color range, device

    /// <summary>Automatic, every pickable mode, then Full-range colors.</summary>
    private void ShowModeMenu(Box anchor)
    {
        if (playback is not { } playing) return;
        var device = playing.Device;
        var choice = ModeChoiceFor(device);
        var modes = modesByDevice.TryGetValue(device.UniqueId, out var known) ? VideoModes.PickableModes(known) : [];
        var expands = settings.ExpandsLimitedRange.GetValueOrDefault(device.UniqueId);
        List<MenuItem> items = [new("Automatic", () => SelectMode(null), IsChecked: choice is null)];
        items.AddRange(modes.Select(mode => new MenuItem(mode.Title, () => SelectMode(mode), IsChecked: choice == mode)));
        items.Add(MenuItem.Separator);
        items.Add(new MenuItem("Full-range colors (blacks look gray)", () => SetExpandsLimitedRange(!expands), IsChecked: expands));
        ShowMenu(anchor, items);
    }

    private void ShowDeviceMenu(Box anchor)
    {
        var playingId = playback?.Device.UniqueId;
        ShowMenu(anchor, [.. playable.Select(device => new MenuItem(device.Name, () => SelectDevice(device), IsChecked: device.UniqueId == playingId))]);
    }

    private void ShowMenu(Box anchor, IReadOnlyList<MenuItem> items)
    {
        var point = new System.Drawing.Point((int)(anchor.X * window.Scale), (int)((anchor.Y - 6) * window.Scale));
        PInvoke.ClientToScreen(window.Handle, ref point);
        isMenuOpen = true;
        try
        {
            PopupMenu.Show(window.Handle, items, point.X, point.Y, opensUp: true);
        }
        finally
        {
            isMenuOpen = false;
            ShowControls();
        }
    }

    /// <summary><c>null</c> is Automatic. The app remembers it per card; the command uses it until it quits, as <c>--mode</c>.</summary>
    private void SelectMode(ModeChoice? choice)
    {
        if (playback is not { Device: var device }) return;
        if (launchMode == LaunchMode.Command)
        {
            options = options with { Mode = choice };
        }
        else
        {
            if (choice is { } chosen) settings.ModeChoices[device.UniqueId] = chosen.Key;
            else settings.ModeChoices.Remove(device.UniqueId);
            settings.Save();
        }
        _ = RestartAsync(device);
    }

    private void SetExpandsLimitedRange(bool isOn)
    {
        if (playback is not { Device: var device }) return;
        settings.ExpandsLimitedRange[device.UniqueId] = isOn;
        settings.Save();
        // The video processor applies it from the next frame; nothing needs to reopen.
        presenter.ExpandsLimitedRange = isOn;
    }

    private void SelectDevice(DeviceInfo device)
    {
        settings.PreferredDeviceId = device.UniqueId;
        settings.Save();
        if (playback?.Device != device) _ = RestartAsync(device);
    }

    // MARK: - Window

    private void ToggleFullScreen()
    {
        window.ToggleFullScreen();
        window.Invalidate();
    }

    /// <summary>Keeps the window's width and fits its height to the video, so there are no black bars.</summary>
    private void FitWindowHeight(VideoMode mode)
    {
        if (window.IsFullScreen || window.IsMaximized || mode.Height <= 0) return;
        var width = window.ClientSize.Width / window.Scale;
        window.ResizeClient(width, width * mode.Height / mode.Width);
    }

    /// <summary>Ctrl+0: one video pixel per screen pixel.</summary>
    private void ShowActualSize()
    {
        if (playback is not { } playing) return;
        window.ResizeClient(playing.Mode.Width / window.Scale, playing.Mode.Height / window.Scale);
    }

    private void Zoom(float factor)
    {
        var (width, height) = window.ClientSize;
        window.ResizeClient(width / window.Scale * factor, height / window.Scale * factor);
    }

    private void ToggleKeepOnTop()
    {
        settings.KeepsOnTop = !settings.KeepsOnTop;
        settings.Save();
        window.SetTopmost(settings.KeepsOnTop);
        ShowToast(Glyphs.Check, settings.KeepsOnTop ? "Kept on top" : "No longer on top", isWarning: false);
    }

    // MARK: - Keys

    private float MouseX => MousePosition().X;

    private float MouseY => MousePosition().Y;

    private (float X, float Y) MousePosition()
    {
        PInvoke.GetCursorPos(out var point);
        PInvoke.ScreenToClient(window.Handle, ref point);
        return (point.X / window.Scale, point.Y / window.Scale);
    }

    /// <summary>The spec's keys, Windows column.</summary>
    private bool HandleKey(KeyPress key)
    {
        var plain = !key.IsControlDown && !key.IsAltDown;
        switch (key.Key)
        {
            case VIRTUAL_KEY.VK_F or VIRTUAL_KEY.VK_F11 when plain:
            case VIRTUAL_KEY.VK_RETURN when key.IsAltDown:
                ToggleFullScreen();
                return true;
            case VIRTUAL_KEY.VK_ESCAPE when window.IsFullScreen:
                ToggleFullScreen();
                return true;
            case VIRTUAL_KEY.VK_M when plain:
                ToggleMute();
                return true;
            case VIRTUAL_KEY.VK_S when plain:
                TakeScreenshot();
                return true;
            case VIRTUAL_KEY.VK_UP or VIRTUAL_KEY.VK_DOWN when plain:
                SetVolume(volumePercent + (key.Key == VIRTUAL_KEY.VK_UP ? Tuning.VolumeStepPercent : -Tuning.VolumeStepPercent));
                Console.WriteLine($"Volume {volumePercent}%");
                ShowControls();
                return true;
            case VIRTUAL_KEY.VK_0 or VIRTUAL_KEY.VK_NUMPAD0 when key.IsControlDown:
                ShowActualSize();
                return true;
            case VIRTUAL_KEY.VK_OEM_PLUS or VIRTUAL_KEY.VK_ADD when key.IsControlDown:
                Zoom(ZoomStep);
                return true;
            case VIRTUAL_KEY.VK_OEM_MINUS or VIRTUAL_KEY.VK_SUBTRACT when key.IsControlDown:
                Zoom(1 / ZoomStep);
                return true;
            case VIRTUAL_KEY.VK_T when key.IsControlDown && key.IsShiftDown:
                ToggleKeepOnTop();
                return true;
            case VIRTUAL_KEY.VK_OEM_COMMA when key.IsControlDown:
                ShowSettingsMenu();
                return true;
            // Closing waits for the key's release: a window hidden while the key is down can get the key again when shown.
            case VIRTUAL_KEY.VK_W when key.IsControlDown:
            case VIRTUAL_KEY.VK_Q when !key.IsAltDown:
                isCloseKeyDown = true;
                closeKeyQuits = key.Key == VIRTUAL_KEY.VK_Q;
                return true;
            default:
                return false;
        }
    }

    private bool closeKeyQuits;

    private void HandleKeyRelease(KeyPress key)
    {
        if (key.Key is not (VIRTUAL_KEY.VK_W or VIRTUAL_KEY.VK_Q) || !isCloseKeyDown) return;
        isCloseKeyDown = false;
        if (closeKeyQuits) Quit();
        else window.Close();
    }
}
