using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Plugview.Core.Launcher;
using Plugview.Core.Selection;
using Plugview.Windows.Ui;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Plugview.Windows.App;

/// <summary>
/// The app's tray icon (the Mac's menu bar icon): its state, a menu with the settings, opening when a card is plugged
/// in, and launch at login. The command has none: closing its window quits.
/// </summary>
internal sealed partial class AppController
{
    private const uint TrayCallback = PInvoke.WM_APP + 2;

    /// <summary>NIN_SELECT | NINF_KEY: the icon was chosen with the keyboard.</summary>
    private const uint TrayKeySelect = PInvoke.NIN_SELECT | 0x1;
    private const string StartupTaskId = "plugview";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly uint TaskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

    private bool isTrayShown;
    private string trayIconName = "";
    private HICON trayIcon;

    private bool HasTray => launchMode == LaunchMode.App;

    private void StartTray()
    {
        if (!HasTray) return;
        window.ExtraMessages = OnTrayMessage;
        UpdateTray();
    }

    /// <summary>Shows the icon for the state: the port while waiting, its triangle outlined with a signal, filled while playing.</summary>
    private unsafe void UpdateTray()
    {
        if (!HasTray) return;
        var (state, tooltip) = this.state switch
        {
            LauncherState.Playing playing => ("playing", $"plugview · {playing.Device.Name}"),
            LauncherState.Ready ready => ("signal", $"plugview · {ready.Devices[0].Name}"),
            LauncherState.Failed => ("empty", "plugview · could not start the card"),
            _ => ("empty", "plugview · waiting for a card"),
        };
        var name = $"tray-{state}-{(TaskbarIsLight() ? "dark" : "light")}";
        if (name != trayIconName)
        {
            var next = LoadTrayIcon(name);
            if (next != HICON.Null)
            {
                if (trayIcon != HICON.Null) PInvoke.DestroyIcon(trayIcon);
                trayIcon = next;
                trayIconName = name;
            }
        }
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = window.Handle,
            uID = 1,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP,
            uCallbackMessage = TrayCallback,
            hIcon = trayIcon,
        };
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (!isTrayShown)
        {
            isTrayShown = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
            data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
            if (isTrayShown) PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
            return;
        }
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
    }

    private unsafe void StopTray()
    {
        if (isTrayShown)
        {
            var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = window.Handle, uID = 1 };
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            isTrayShown = false;
        }
        if (trayIcon != HICON.Null) PInvoke.DestroyIcon(trayIcon);
        trayIcon = HICON.Null;
    }

    private LRESULT? OnTrayMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (AppHost.IsActivateMessage(message))
        {
            // Another launch from the Start menu: show this one instead.
            OpenWindow();
            return new LRESULT(0);
        }
        if (message == TaskbarCreated)
        {
            // Explorer restarted and forgot every tray icon.
            isTrayShown = false;
            UpdateTray();
            return new LRESULT(0);
        }
        if (message == PInvoke.WM_SETTINGCHANGE)
        {
            // The taskbar switched between light and dark.
            UpdateTray();
            return null;
        }
        if (message != TrayCallback) return null;
        // NOTIFYICON_VERSION_4: the event in the low word, the anchor point in wParam.
        switch ((uint)(lParam.Value & 0xFFFF))
        {
            case PInvoke.NIN_SELECT:
            case TrayKeySelect:
                OpenWindow();
                break;
            case PInvoke.WM_CONTEXTMENU:
                ShowTrayMenu((short)(wParam.Value & 0xFFFF), (short)((wParam.Value >> 16) & 0xFFFF));
                break;
            default:
                break;
        }
        return new LRESULT(0);
    }

    /// <summary>Ctrl+, in the window: the same settings, under the pointer.</summary>
    private void ShowSettingsMenu()
    {
        if (!HasTray) return;
        PInvoke.GetCursorPos(out var point);
        ShowTrayMenu(point.X, point.Y);
    }

    private void ShowTrayMenu(int x, int y)
    {
        var status = state switch
        {
            LauncherState.Playing playing => $"Playing {playing.Device.Name}",
            LauncherState.Ready ready => $"Signal from {ready.Devices[0].Name}",
            _ => "Waiting for a capture card",
        };
        var launchesAtLogin = LaunchesAtLogin();
        PopupMenu.Show(window.Handle,
        [
            new MenuItem(status, IsEnabled: false),
            MenuItem.Separator,
            new MenuItem("Open plugview", OpenWindow),
            MenuItem.Separator,
            new MenuItem("Open when a card is plugged in", () => Toggle(() => settings.OpensOnPlugIn = !settings.OpensOnPlugIn), IsChecked: settings.OpensOnPlugIn),
            new MenuItem("Stay in the tray when closed", () => Toggle(() => settings.KeepsRunningInTray = !settings.KeepsRunningInTray), IsChecked: settings.KeepsRunningInTray),
            new MenuItem("Launch at login", () => _ = SetLaunchesAtLoginAsync(!launchesAtLogin), IsChecked: launchesAtLogin),
            MenuItem.Separator,
            new MenuItem("Quit plugview", Quit),
        ], x, y, opensUp: true);
    }

    private void Toggle(Action change)
    {
        change();
        settings.Save();
    }

    /// <summary>While plugview waits in the tray, a newly plugged card opens the window (and plays).</summary>
    private void OpenOnPlugIn(IReadOnlyList<DeviceInfo> previous, IReadOnlyList<DeviceInfo> now)
    {
        if (!HasTray || !settings.OpensOnPlugIn || isQuitting) return;
        if (now.Any(device => !previous.Contains(device))) OpenWindow();
    }

    // MARK: - Launch at login

    /// <summary>The Store app uses its package's StartupTask (Settings › Apps › Startup lists it); the download, the Run key.</summary>
    private static bool LaunchesAtLogin()
    {
        if (Permissions.IsPackaged)
        {
            try
            {
                var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(StartupTaskId) is string;
    }

    private async Task SetLaunchesAtLoginAsync(bool isOn)
    {
        try
        {
            if (Permissions.IsPackaged)
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (!isOn)
                {
                    task.Disable();
                    return;
                }
                if (await task.RequestEnableAsync() is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy)
                {
                    ShowToast(Glyphs.Warning, "Turn plugview on in Settings › Apps › Startup", isWarning: true, "Open", () => Permissions.OpenUri("ms-settings:startupapps"));
                }
                return;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (isOn) key.SetValue(StartupTaskId, $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue(StartupTaskId, throwOnMissingValue: false);
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Console.Error.WriteLine($"plugview: launch at login: {error.Message}");
        }
    }

    // MARK: - Icons

    /// <summary>A light taskbar needs the dark icon (the bone one would vanish on it).</summary>
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    /// <summary>The embedded <c>.ico</c>'s image nearest the tray's size at this DPI.</summary>
    private static unsafe HICON LoadTrayIcon(string name)
    {
        using var stream = typeof(AppController).Assembly.GetManifestResourceStream($"Plugview.Windows.Assets.{name}.ico");
        if (stream is null) return HICON.Null;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        var size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        if (IconImage(bytes, size) is not { } image) return HICON.Null;
        fixed (byte* data = image.Span)
        {
            return PInvoke.CreateIconFromResourceEx(data, (uint)image.Length, true, 0x00030000, size, size, IMAGE_FLAGS.LR_DEFAULTCOLOR);
        }
    }

    /// <summary>
    /// Picks the image in an ICO file closest to <paramref name="size"/> (preferring larger, scaled down): the directory
    /// is 6 bytes, then 16 per image with its width at 0 (0 = 256), byte count at 8 and offset at 12.
    /// </summary>
    internal static ReadOnlyMemory<byte>? IconImage(byte[] ico, int size)
    {
        if (ico.Length < 6) return null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        ReadOnlyMemory<byte>? best = null;
        var bestScore = int.MaxValue;
        for (var index = 0; index < count && 6 + (16 * (index + 1)) <= ico.Length; index++)
        {
            var entry = ico.AsSpan(6 + (16 * index), 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset < 0 || length <= 0 || offset + length > ico.Length) continue;
            // Downscaling looks better than upscaling: a smaller image counts as much further away.
            var score = width >= size ? width - size : (size - width) * 4;
            if (score >= bestScore) continue;
            bestScore = score;
            best = ico.AsMemory(offset, length);
        }
        return best;
    }
}
