using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Plugview.Windows.Graphics;
using Plugview.Windows.Ui;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Plugview.Windows.App;

/// <summary>A key with the modifiers held when it was pressed.</summary>
internal readonly record struct KeyPress(VIRTUAL_KEY Key, bool IsControlDown, bool IsShiftDown, bool IsAltDown);

/// <summary>What the window shows and how it reacts; <see cref="AppController"/> implements it.</summary>
internal interface IWindowContent
{
    /// <summary>Draws over the video; false leaves nothing over it (controls hidden while playing).</summary>
    bool Draw(Canvas canvas, UiFrame frame, Interaction interaction);

    /// <summary>Something moves: the window redraws at the display's pace until it is false.</summary>
    bool IsAnimating { get; }

    /// <summary>The window buttons' opacity: 1 on the launcher, fading with the controls while playing.</summary>
    float ChromeOpacity { get; }

    /// <summary>The pointer hides with the controls, so nothing sits over the game.</summary>
    bool HidesPointer { get; }

    /// <summary>A key the window did not use itself (Tab, Enter and Space go to focus first); true if used.</summary>
    bool KeyDown(KeyPress key);

    void KeyUp(KeyPress key);

    void PointerMoved();

    void PointerLeft();

    /// <summary>The wheel turned over the window; <paramref name="notches"/> is positive away from the user.</summary>
    void Wheel(float notches, UiElement? over);

    /// <summary>The window is asked to close; true keeps it open (the content hid it, or will).</summary>
    bool Closing();

    /// <summary>The window's size or full-screen state changed.</summary>
    void Resized();
}

/// <summary>
/// plugview's window: a dark surface with no title bar, its own window buttons (with Snap Layouts on Windows 11), the
/// video's swap chain underneath and the <see cref="Overlay"/> on top. Dragging anywhere that is not a control moves it.
/// </summary>
internal sealed unsafe class AppWindow : IDisposable
{
    /// <summary>The app's window class, which a second launch looks for; the command's windows use another.</summary>
    public const string AppClassName = "plugview.app";
    public const string CommandClassName = "plugview.command";
    private const nuint AnimationTimer = 1;
    private const float CaptionButtonWidth = 46, CaptionHeight = 32;
    private static readonly Dictionary<nint, AppWindow> Windows = [];

    private readonly UiThread uiThread;
    private readonly Interaction interaction = new();
    private UiFrame frame = new();
    private bool isFullScreen;
    private WINDOWPLACEMENT windowedPlacement;
    private bool isAnimationTimerOn;
    private bool isTrackingLeave;
    private bool isTrackingNonClientLeave;
    private string? nonClientPressed;
    private (float X, float Y)? lastPointer;

    public AppWindow(UiThread uiThread, string className, string title, int width, int height)
    {
        this.uiThread = uiThread;
        PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        var cathode = Palette.Cathode;
        fixed (char* classNameText = className)
        fixed (char* windowTitle = title)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW | WNDCLASS_STYLES.CS_DBLCLKS,
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                // Cathode before the first frame is drawn, so the window never flashes white.
                hbrBackground = PInvoke.CreateSolidBrush(new COLORREF(Rgb(cathode))),
                lpszClassName = classNameText,
            };
            PInvoke.RegisterClassEx(&windowClass);
            Handle = PInvoke.CreateWindowEx(
                0, classNameText, windowTitle, WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                PInvoke.CW_USEDEFAULT, PInvoke.CW_USEDEFAULT, width, height, default, default, instance, null);
        }
        if (Handle == HWND.Null) throw new InvalidOperationException($"cannot create the window (error {Marshal.GetLastPInvokeError()})");
        Windows[(nint)Handle.Value] = this;

        // Dark caption and borders in light mode too: the window is a CRT surface (brand.json "window").
        BOOL isDark = true;
        PInvoke.DwmSetWindowAttribute(Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &isDark, (uint)sizeof(BOOL));
        Overlay = new Overlay(Handle) { Dpi = PInvoke.GetDpiForWindow(Handle) };
        // Re-runs WM_NCCALCSIZE so the title bar goes away before the window is shown.
        PInvoke.SetWindowPos(Handle, default, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        uiThread.Attach(Handle);
    }

    public HWND Handle { get; }

    public Overlay Overlay { get; }

    public IWindowContent? Content { get; set; }

    /// <summary>The elements the last frame put on screen (for Narrator).</summary>
    public UiFrame Frame => frame;

    public Interaction Interaction => interaction;

    /// <summary>The client area changed size (pixels), on the window's thread.</summary>
    public event Action<uint, uint>? Resized;

    /// <summary>Windows asks for the accessibility tree (WM_GETOBJECT); the handler answers or returns null.</summary>
    public Func<WPARAM, LPARAM, LRESULT?>? AccessibilityRequested { get; set; }

    /// <summary>The computer is going to sleep (true) or woke up (false).</summary>
    public event Action<bool>? PowerChanged;

    public bool IsFullScreen => isFullScreen;

    public bool IsVisible => PInvoke.IsWindowVisible(Handle);

    public bool IsMaximized => PInvoke.IsZoomed(Handle);

    public (uint Width, uint Height) ClientSize
    {
        get
        {
            PInvoke.GetClientRect(Handle, out var client);
            return ((uint)(client.right - client.left), (uint)(client.bottom - client.top));
        }
    }

    /// <summary>DIPs per pixel inverse: pixels = DIPs × <see cref="Scale"/>.</summary>
    public float Scale => Overlay.Dpi / 96f;

    public void Show()
    {
        PInvoke.ShowWindow(Handle, PInvoke.IsIconic(Handle) ? SHOW_WINDOW_CMD.SW_RESTORE : SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetForegroundWindow(Handle);
        Invalidate();
    }

    public void Hide() => PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);

    public void SetTitle(string title) => PInvoke.SetWindowText(Handle, title);

    /// <summary>Closes the window from any thread (as the close button does: <see cref="IWindowContent.Closing"/> may keep it).</summary>
    public void Close() => PInvoke.PostMessage(Handle, PInvoke.WM_CLOSE, default, default);

    /// <summary>Destroys the window for good; the message loop ends.</summary>
    public void Destroy() => PInvoke.DestroyWindow(Handle);

    /// <summary>Redraws the overlay at the next paint.</summary>
    public void Invalidate() => PInvoke.InvalidateRect(Handle, (RECT?)null, false);

    /// <summary>Pumps messages until the window is destroyed.</summary>
    public static void RunMessageLoop()
    {
        MSG message;
        while (PInvoke.GetMessage(&message, default, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
    }

    /// <summary>Borderless over the whole monitor, or back to the window it was.</summary>
    public void ToggleFullScreen()
    {
        if (isFullScreen)
        {
            PInvoke.SetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, unchecked((int)(WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_VISIBLE)));
            isFullScreen = false;
            var placement = windowedPlacement;
            PInvoke.SetWindowPlacement(Handle, &placement);
            PInvoke.SetWindowPos(Handle, default, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        }
        else
        {
            windowedPlacement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            var placement = windowedPlacement;
            PInvoke.GetWindowPlacement(Handle, &placement);
            windowedPlacement = placement;
            var monitor = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            PInvoke.GetMonitorInfo(PInvoke.MonitorFromWindow(Handle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST), ref monitor);
            // A borderless window covering the monitor gets independent flip: frames go to the display without composition.
            isFullScreen = true;
            PInvoke.SetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, unchecked((int)(WINDOW_STYLE.WS_POPUP | WINDOW_STYLE.WS_VISIBLE)));
            var area = monitor.rcMonitor;
            PInvoke.SetWindowPos(Handle, default, area.left, area.top, area.right - area.left, area.bottom - area.top, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
        }
        Content?.Resized();
    }

    public void SetTopmost(bool isTopmost) =>
        PInvoke.SetWindowPos(Handle, isTopmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);

    /// <summary>Resizes the client area to <paramref name="width"/>×<paramref name="height"/> DIPs, kept on the monitor.</summary>
    public void ResizeClient(float width, float height)
    {
        if (isFullScreen) return;
        if (IsMaximized) PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_RESTORE);
        var monitor = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(PInvoke.MonitorFromWindow(Handle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST), ref monitor);
        var work = monitor.rcWork;
        var pixelsWidth = width * Scale;
        var pixelsHeight = height * Scale;
        var fit = Math.Min(1, Math.Min((work.right - work.left) / pixelsWidth, (work.bottom - work.top) / pixelsHeight));
        var (frameWidth, frameHeight) = FrameExtra();
        var newWidth = (int)Math.Round(Math.Max(320 * Scale, pixelsWidth * fit)) + frameWidth;
        var newHeight = (int)Math.Round(Math.Max(180 * Scale, pixelsHeight * fit)) + frameHeight;
        PInvoke.GetWindowRect(Handle, out var current);
        var left = Math.Clamp(current.left, work.left, Math.Max(work.left, work.right - newWidth));
        var top = Math.Clamp(current.top, work.top, Math.Max(work.top, work.bottom - newHeight));
        PInvoke.SetWindowPos(Handle, default, left, top, newWidth, newHeight, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>The window's position and client size in pixels, for remembering it.</summary>
    public (int Left, int Top, int Width, int Height)? RestoredFrame
    {
        get
        {
            if (isFullScreen) return null;
            var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            PInvoke.GetWindowPlacement(Handle, &placement);
            var bounds = placement.rcNormalPosition;
            return (bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top);
        }
        set
        {
            if (value is not { } frameBounds) return;
            var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            PInvoke.GetWindowPlacement(Handle, &placement);
            placement.rcNormalPosition = new RECT { left = frameBounds.Left, top = frameBounds.Top, right = frameBounds.Left + frameBounds.Width, bottom = frameBounds.Top + frameBounds.Height };
            placement.showCmd = SHOW_WINDOW_CMD.SW_HIDE;
            // Windows moves a placement that is off every monitor back onto one.
            PInvoke.SetWindowPlacement(Handle, &placement);
        }
    }

    /// <summary>The window's width and height beyond the client area (the side and bottom resize borders).</summary>
    private (int Width, int Height) FrameExtra()
    {
        PInvoke.GetWindowRect(Handle, out var window);
        PInvoke.GetClientRect(Handle, out var client);
        return (window.right - window.left - client.right, window.bottom - window.top - client.bottom);
    }

    // MARK: - Drawing

    private void Render()
    {
        var (width, height) = ClientSize;
        var content = Content;
        var nextFrame = new UiFrame();
        var drawsAnything = false;
        Overlay.Draw(width, height, canvas =>
        {
            drawsAnything = content?.Draw(canvas, nextFrame, interaction) ?? false;
            var chromeOpacity = content?.ChromeOpacity ?? 1;
            if (!isFullScreen && chromeOpacity > 0)
            {
                drawsAnything = true;
                DrawCaptionButtons(canvas, nextFrame, chromeOpacity);
            }
        });
        // Nothing to show: empty the layer, so the video can flip straight to the display.
        if (!drawsAnything) Overlay.Draw(width, height, null);
        frame = nextFrame;
        UpdateAnimationTimer();
    }

    private void UpdateAnimationTimer()
    {
        // Nothing animates for nobody: a window in the tray or minimized draws nothing until it is back.
        var isAnimating = (Content?.IsAnimating ?? false) && IsVisible && !PInvoke.IsIconic(Handle);
        if (isAnimating == isAnimationTimerOn) return;
        isAnimationTimerOn = isAnimating;
        // USER_TIMER_MINIMUM is 10 ms; the timer rounds to the system tick, close to the display's 60 Hz pace.
        if (isAnimating) PInvoke.SetTimer(Handle, AnimationTimer, 10, null);
        else PInvoke.KillTimer(Handle, AnimationTimer);
    }

    private void DrawCaptionButtons(Canvas canvas, UiFrame target, float opacity)
    {
        canvas.PushOpacity(opacity);
        var buttons = CaptionButtons(canvas.Width);
        foreach (var (id, box, glyph, name) in buttons)
        {
            var isHovered = interaction.IsHovered(id) || nonClientPressed == id;
            if (isHovered) canvas.FillRectangle(box, id == "close" ? new Rgba(0.77f, 0.17f, 0.11f) : new Rgba(1, 1, 1, 0.08f));
            canvas.Icon(glyph, 10, box, Palette.Bone);
            Widgets.FocusRing(canvas, interaction, id, box.Inset(4), 4);
            // Narrator reads them; clicks arrive as non-client messages (Snap Layouts need HTMAXBUTTON).
            target.Add(new UiElement(id, box, UiRole.Button, name, () => CaptionAction(id)));
        }
        canvas.PopLayer();
    }

    private (string Id, Box Box, char Glyph, string Name)[] CaptionButtons(float width) =>
    [
        ("minimize", new Box(width - (3 * CaptionButtonWidth), 0, CaptionButtonWidth, CaptionHeight), Glyphs.Minimize, "Minimize"),
        ("maximize", new Box(width - (2 * CaptionButtonWidth), 0, CaptionButtonWidth, CaptionHeight), IsMaximized ? Glyphs.Restore : Glyphs.Maximize, IsMaximized ? "Restore" : "Maximize"),
        ("close", new Box(width - CaptionButtonWidth, 0, CaptionButtonWidth, CaptionHeight), Glyphs.Close, "Close"),
    ];

    private void CaptionAction(string id)
    {
        switch (id)
        {
            case "minimize": PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_MINIMIZE); break;
            case "maximize": PInvoke.ShowWindow(Handle, IsMaximized ? SHOW_WINDOW_CMD.SW_RESTORE : SHOW_WINDOW_CMD.SW_MAXIMIZE); break;
            case "close": Close(); break;
            default: throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown caption button");
        }
    }

    // MARK: - Messages

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (Windows.TryGetValue((nint)window.Value, out var appWindow) && appWindow.OnMessage(message, wParam, lParam) is { } handled) return handled;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An exception must never cross into Windows' message dispatch; report it and let the default run.
            Console.Error.WriteLine($"plugview: {error.Message}");
            Debug.WriteLine(error);
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    /// <summary>Messages for other parts of the app (the tray icon's callbacks); answered first when they return a value.</summary>
    public Func<uint, WPARAM, LPARAM, LRESULT?>? ExtraMessages { get; set; }

    private LRESULT? OnMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (ExtraMessages?.Invoke(message, wParam, lParam) is { } extra) return extra;
        switch (message)
        {
            case UiThread.WorkMessage:
                uiThread.Drain();
                return new LRESULT(0);
            case PInvoke.WM_NCCALCSIZE when wParam.Value != 0 && !isFullScreen:
                return RemoveTitleBar(wParam, lParam);
            case PInvoke.WM_NCHITTEST:
                return HitTest(wParam, lParam);
            case PInvoke.WM_PAINT:
                var paint = default(PAINTSTRUCT);
                PInvoke.BeginPaint(Handle, &paint);
                PInvoke.EndPaint(Handle, &paint);
                Render();
                return new LRESULT(0);
            case PInvoke.WM_ERASEBKGND:
                // The overlay and the swap chain cover the window; erasing would only flicker.
                return new LRESULT(1);
            case PInvoke.WM_TIMER when wParam.Value == AnimationTimer:
                Invalidate();
                return new LRESULT(0);
            case PInvoke.WM_SIZE:
                Resized?.Invoke((uint)(lParam.Value & 0xFFFF), (uint)((lParam.Value >> 16) & 0xFFFF));
                Content?.Resized();
                Render();
                return new LRESULT(0);
            case PInvoke.WM_DPICHANGED:
                Overlay.Dpi = (uint)(wParam.Value & 0xFFFF);
                var suggested = (RECT*)lParam.Value;
                PInvoke.SetWindowPos(Handle, default, suggested->left, suggested->top, suggested->right - suggested->left, suggested->bottom - suggested->top, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
                return new LRESULT(0);
            case PInvoke.WM_GETMINMAXINFO:
                var limits = (MINMAXINFO*)lParam.Value;
                limits->ptMinTrackSize.X = (int)(320 * Scale);
                limits->ptMinTrackSize.Y = (int)(180 * Scale);
                return new LRESULT(0);
            case PInvoke.WM_MOUSEMOVE:
                if (frame.Find(interaction.PressedId) is { Drag: { } dragging }) dragging(Point(lParam).X);
                OnPointer(Point(lParam), isNonClient: false);
                return new LRESULT(0);
            case PInvoke.WM_NCMOUSEMOVE:
                OnPointer(ScreenPoint(lParam), isNonClient: true);
                return null;
            case PInvoke.WM_MOUSELEAVE:
            case PInvoke.WM_NCMOUSELEAVE:
                if (message == PInvoke.WM_MOUSELEAVE) isTrackingLeave = false;
                else isTrackingNonClientLeave = false;
                OnPointerLeft();
                return new LRESULT(0);
            case PInvoke.WM_LBUTTONDOWN:
                PInvoke.SetCapture(Handle);
                var down = frame.HitTest(Point(lParam).X, Point(lParam).Y);
                interaction.PressedId = down?.Id;
                down?.Drag?.Invoke(Point(lParam).X);
                interaction.ShowsFocus = false;
                Invalidate();
                return new LRESULT(0);
            case PInvoke.WM_LBUTTONUP:
                PInvoke.ReleaseCapture();
                var released = frame.HitTest(Point(lParam).X, Point(lParam).Y);
                var pressed = interaction.PressedId;
                interaction.PressedId = null;
                Invalidate();
                if (released is not null && released.Id == pressed) released.Invoke?.Invoke();
                return new LRESULT(0);
            case PInvoke.WM_NCLBUTTONDOWN when CaptionId((uint)wParam.Value) is { } downId:
                // Windows would otherwise draw and track the classic buttons over ours.
                nonClientPressed = downId;
                Invalidate();
                return new LRESULT(0);
            case PInvoke.WM_NCLBUTTONUP when CaptionId((uint)wParam.Value) is { } upId:
                var wasPressed = nonClientPressed == upId;
                nonClientPressed = null;
                Invalidate();
                if (wasPressed) CaptionAction(upId);
                return new LRESULT(0);
            case PInvoke.WM_MOUSEWHEEL:
                var notches = (short)((wParam.Value >> 16) & 0xFFFF) / 120f;
                var cursor = ScreenPoint(lParam);
                Content?.Wheel(notches, frame.HitTest(cursor.X, cursor.Y));
                return new LRESULT(0);
            case PInvoke.WM_SETCURSOR when (lParam.Value & 0xFFFF) == PInvoke.HTCLIENT || (lParam.Value & 0xFFFF) == PInvoke.HTCAPTION:
                if (Content?.HidesPointer != true) return null;
                PInvoke.SetCursor(default);
                return new LRESULT(1);
            case PInvoke.WM_KEYDOWN:
            case PInvoke.WM_SYSKEYDOWN:
                return OnKeyDown(Key(wParam, message == PInvoke.WM_SYSKEYDOWN)) ? new LRESULT(0) : null;
            case PInvoke.WM_KEYUP:
            case PInvoke.WM_SYSKEYUP:
                Content?.KeyUp(Key(wParam, message == PInvoke.WM_SYSKEYUP));
                return null;
            case PInvoke.WM_GETOBJECT:
                return AccessibilityRequested?.Invoke(wParam, lParam);
            case PInvoke.WM_POWERBROADCAST:
                // PBT_APMSUSPEND, PBT_APMRESUMEAUTOMATIC.
                if (wParam.Value == 0x4) PowerChanged?.Invoke(true);
                else if (wParam.Value == 0x12) PowerChanged?.Invoke(false);
                return new LRESULT(1);
            case PInvoke.WM_CLOSE:
                if (Content?.Closing() == true) return new LRESULT(0);
                PInvoke.DestroyWindow(Handle);
                return new LRESULT(0);
            case PInvoke.WM_DESTROY:
                Windows.Remove((nint)Handle.Value);
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
            default:
                return null;
        }
    }

    /// <summary>
    /// Keeps the side and bottom resize borders Windows computes but gives the title bar's space to the client area
    /// (as Windows Terminal does). Maximized windows hang over the monitor's edge by the border; that part is trimmed.
    /// </summary>
    private LRESULT RemoveTitleBar(WPARAM wParam, LPARAM lParam)
    {
        var parameters = (NCCALCSIZE_PARAMS*)lParam.Value;
        var originalTop = parameters->rgrc._0.top;
        var result = PInvoke.DefWindowProc(Handle, PInvoke.WM_NCCALCSIZE, wParam, lParam);
        parameters->rgrc._0.top = originalTop + (IsMaximized ? ResizeBorderHeight() : 0);
        return result;
    }

    private int ResizeBorderHeight()
    {
        var dpi = PInvoke.GetDpiForWindow(Handle);
        return PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYFRAME, dpi) + PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, dpi);
    }

    private LRESULT HitTest(WPARAM wParam, LPARAM lParam)
    {
        var standard = PInvoke.DefWindowProc(Handle, PInvoke.WM_NCHITTEST, wParam, lParam);
        if (standard.Value != PInvoke.HTCLIENT || isFullScreen) return standard;
        var point = ScreenPoint(lParam);
        // The top resize border lives in the client area now that the title bar is gone.
        if (!IsMaximized && point.Y * Scale < ResizeBorderHeight()) return new LRESULT((nint)PInvoke.HTTOP);
        var (width, _) = ClientSize;
        if (Content?.ChromeOpacity > 0)
        {
            foreach (var (id, box, _, _) in CaptionButtons(width / Scale))
            {
                if (!box.Contains(point.X, point.Y)) continue;
                return new LRESULT((nint)(id switch { "minimize" => PInvoke.HTMINBUTTON, "maximize" => PInvoke.HTMAXBUTTON, _ => PInvoke.HTCLOSE }));
            }
        }
        // Controls take clicks; everything else moves the window (drag the video, as on the Mac).
        return new LRESULT((nint)(frame.HitTest(point.X, point.Y) is null ? PInvoke.HTCAPTION : PInvoke.HTCLIENT));
    }

    private static string? CaptionId(uint hit) => hit switch
    {
        PInvoke.HTMINBUTTON => "minimize",
        PInvoke.HTMAXBUTTON => "maximize",
        PInvoke.HTCLOSE => "close",
        _ => null,
    };

    private void OnPointer((float X, float Y) point, bool isNonClient)
    {
        TrackLeave(isNonClient);
        var hovered = isNonClient
            ? CaptionButtons(ClientSize.Width / Scale).FirstOrDefault(button => button.Box.Contains(point.X, point.Y)).Id ?? frame.HitTest(point.X, point.Y)?.Id
            : frame.HitTest(point.X, point.Y)?.Id;
        if (hovered != interaction.HoveredId)
        {
            interaction.HoveredId = hovered;
            Invalidate();
        }
        // Windows also sends moves when nothing moved (a window appearing under the pointer); those wake nothing.
        if (lastPointer == point) return;
        lastPointer = point;
        Content?.PointerMoved();
    }

    private void OnPointerLeft()
    {
        if (isTrackingLeave || isTrackingNonClientLeave) return;
        // Moving between the video (the window's drag area) and a control leaves one part for the other, not the window.
        PInvoke.GetCursorPos(out var cursor);
        PInvoke.GetWindowRect(Handle, out var bounds);
        if (cursor.X >= bounds.left && cursor.X < bounds.right && cursor.Y >= bounds.top && cursor.Y < bounds.bottom) return;
        lastPointer = null;
        interaction.HoveredId = null;
        nonClientPressed = null;
        Invalidate();
        Content?.PointerLeft();
    }

    private void TrackLeave(bool isNonClient)
    {
        if (isNonClient ? isTrackingNonClientLeave : isTrackingLeave) return;
        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE | (isNonClient ? TRACKMOUSEEVENT_FLAGS.TME_NONCLIENT : 0),
            hwndTrack = Handle,
        };
        PInvoke.TrackMouseEvent(ref track);
        if (isNonClient) isTrackingNonClientLeave = true;
        else isTrackingLeave = true;
    }

    /// <summary>Tab moves keyboard focus between controls, Enter and Space press the focused one; other keys go to the content.</summary>
    private bool OnKeyDown(KeyPress key)
    {
        switch (key.Key)
        {
            case VIRTUAL_KEY.VK_TAB when !key.IsAltDown && !key.IsControlDown:
                var next = frame.NextFocus(interaction.FocusedId, key.IsShiftDown);
                interaction.FocusedId = next?.Id;
                interaction.ShowsFocus = next is not null;
                Content?.PointerMoved();
                Invalidate();
                FocusChanged?.Invoke(next);
                return true;
            case VIRTUAL_KEY.VK_RETURN or VIRTUAL_KEY.VK_SPACE when !key.IsAltDown && interaction.ShowsFocus && frame.Find(interaction.FocusedId) is { Invoke: { } invoke }:
                invoke();
                return true;
            default:
                return Content?.KeyDown(key) ?? false;
        }
    }

    /// <summary>Keyboard focus moved to an element (Narrator follows it).</summary>
    public event Action<UiElement?>? FocusChanged;

    private static KeyPress Key(WPARAM wParam, bool isAltDown) => new(
        (VIRTUAL_KEY)wParam.Value,
        PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0,
        PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0,
        isAltDown);

    /// <summary>A client-area mouse position in DIPs.</summary>
    private (float X, float Y) Point(LPARAM lParam) => ((short)(lParam.Value & 0xFFFF) / Scale, (short)((lParam.Value >> 16) & 0xFFFF) / Scale);

    /// <summary>A screen position (non-client messages, the wheel, hit tests) in client DIPs.</summary>
    private (float X, float Y) ScreenPoint(LPARAM lParam)
    {
        var point = new System.Drawing.Point((short)(lParam.Value & 0xFFFF), (short)((lParam.Value >> 16) & 0xFFFF));
        PInvoke.ScreenToClient(Handle, ref point);
        return (point.X / Scale, point.Y / Scale);
    }

    private static uint Rgb(Rgba color) => (uint)(color.Red * 255) | ((uint)(color.Green * 255) << 8) | ((uint)(color.Blue * 255) << 16);

    public void Dispose()
    {
        if (isAnimationTimerOn) PInvoke.KillTimer(Handle, AnimationTimer);
        Overlay.Dispose();
        Windows.Remove((nint)Handle.Value);
    }
}
