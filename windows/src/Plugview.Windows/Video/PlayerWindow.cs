using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Power;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Plugview.Windows.Video;

/// <summary>What a key in the player window asks for (spec: "Keys in the window", Windows column).</summary>
internal enum PlayerKey
{
    ToggleFullScreen,
    LeaveFullScreen,
    ToggleMute,
    VolumeUp,
    VolumeDown,
    Quit,
}

/// <summary>The plain Win32 window the latency spike draws into: black, resizable, borderless in full screen.</summary>
/// <remarks>
/// The WinUI 3 app (phase 6.3) replaces it with a <c>SwapChainPanel</c>; this one has no framework between the swap
/// chain and the compositor, which makes it the baseline the app's latency is compared with.
/// </remarks>
internal sealed unsafe class PlayerWindow
{
    private const string ClassName = "plugview.player";
    private static PlayerWindow? current;

    private WINDOW_STYLE windowedStyle;
    private RECT windowedFrame;
    private bool isFullScreen;

    public HWND Handle { get; }

    /// <summary>The client area changed size (width, height), on the window's thread.</summary>
    public event Action<uint, uint>? Resized;

    public event Action<PlayerKey>? KeyPressed;

    public PlayerWindow(string title, int clientWidth, int clientHeight)
    {
        current = this;
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        fixed (char* className = ClassName)
        fixed (char* windowTitle = title)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(&windowClass);

            var frame = new RECT { right = clientWidth, bottom = clientHeight };
            PInvoke.AdjustWindowRect(&frame, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, false);
            Handle = PInvoke.CreateWindowEx(
                0, className, windowTitle, WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                PInvoke.CW_USEDEFAULT, PInvoke.CW_USEDEFAULT, frame.right - frame.left, frame.bottom - frame.top,
                default, default, instance, null);
        }
        if (Handle == HWND.Null) throw new InvalidOperationException($"cannot create the player window (error {Marshal.GetLastPInvokeError()})");
    }

    public void SetTitle(string title) => PInvoke.SetWindowText(Handle, title);

    public (uint Width, uint Height) ClientSize
    {
        get
        {
            PInvoke.GetClientRect(Handle, out var client);
            return ((uint)(client.right - client.left), (uint)(client.bottom - client.top));
        }
    }

    /// <summary>Shows the window and pumps messages until it closes; keeps the display awake meanwhile (K32).</summary>
    public void RunUntilClosed()
    {
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
        try
        {
            MSG message;
            while (PInvoke.GetMessage(&message, default, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(&message);
                PInvoke.DispatchMessage(&message);
            }
        }
        finally
        {
            PInvoke.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }
    }

    /// <summary>Closes the window from any thread.</summary>
    public void Close() => PInvoke.PostMessage(Handle, PInvoke.WM_CLOSE, default, default);

    /// <summary>Borderless over the whole monitor, or back to the window it was.</summary>
    public void ToggleFullScreen()
    {
        if (isFullScreen)
        {
            PInvoke.SetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, unchecked((int)windowedStyle));
            PInvoke.SetWindowPos(
                Handle, default, windowedFrame.left, windowedFrame.top, windowedFrame.right - windowedFrame.left, windowedFrame.bottom - windowedFrame.top,
                SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
            isFullScreen = false;
            return;
        }
        windowedStyle = (WINDOW_STYLE)PInvoke.GetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.GetWindowRect(Handle, out windowedFrame);
        var monitor = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(PInvoke.MonitorFromWindow(Handle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST), ref monitor);
        // A borderless window covering the monitor gets independent flip: frames go to the display without composition.
        PInvoke.SetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, unchecked((int)(WINDOW_STYLE.WS_POPUP | WINDOW_STYLE.WS_VISIBLE)));
        var area = monitor.rcMonitor;
        PInvoke.SetWindowPos(
            Handle, default, area.left, area.top, area.right - area.left, area.bottom - area.top,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
        isFullScreen = true;
    }

    public bool IsFullScreen => isFullScreen;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (current is { } player && player.Handle == window && player.OnMessage(message, wParam, lParam) is { } handled) return handled;
        }
        catch (Exception error)
        {
            // An exception must never cross into Windows' message dispatch; report it and let the default run.
            Console.Error.WriteLine($"plugview: {error.Message}");
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private LRESULT? OnMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_SIZE:
                Resized?.Invoke((uint)(lParam.Value & 0xFFFF), (uint)((lParam.Value >> 16) & 0xFFFF));
                return new LRESULT(0);
            case PInvoke.WM_ERASEBKGND:
                // The swap chain covers the window; erasing would only flicker.
                return new LRESULT(1);
            case PInvoke.WM_KEYDOWN:
            case PInvoke.WM_SYSKEYDOWN:
                if (Key((VIRTUAL_KEY)wParam.Value, isAltDown: message == PInvoke.WM_SYSKEYDOWN) is not { } key) return null;
                KeyPressed?.Invoke(key);
                return new LRESULT(0);
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
            default:
                return null;
        }
    }

    private static PlayerKey? Key(VIRTUAL_KEY key, bool isAltDown)
    {
        var isControlDown = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0;
        return key switch
        {
            VIRTUAL_KEY.VK_F or VIRTUAL_KEY.VK_F11 when !isAltDown => PlayerKey.ToggleFullScreen,
            VIRTUAL_KEY.VK_RETURN when isAltDown => PlayerKey.ToggleFullScreen,
            VIRTUAL_KEY.VK_ESCAPE => PlayerKey.LeaveFullScreen,
            VIRTUAL_KEY.VK_M when !isAltDown => PlayerKey.ToggleMute,
            VIRTUAL_KEY.VK_UP => PlayerKey.VolumeUp,
            VIRTUAL_KEY.VK_DOWN => PlayerKey.VolumeDown,
            VIRTUAL_KEY.VK_Q when !isAltDown || isControlDown => PlayerKey.Quit,
            _ => null,
        };
    }
}
