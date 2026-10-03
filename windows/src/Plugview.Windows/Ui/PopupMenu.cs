using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Plugview.Windows.Ui;

/// <summary>One line of a popup menu; a <c>null</c> title is a separator.</summary>
internal sealed record MenuItem(string? Title, Action? Choose = null, bool IsChecked = false, bool IsEnabled = true)
{
    public static MenuItem Separator { get; } = new(Title: null);
}

/// <summary>
/// Windows' own popup menu: keyboard navigation, Narrator and high contrast come with it. Used for the resolution and
/// device menus over the video, and for the tray icon.
/// </summary>
internal static unsafe class PopupMenu
{
    /// <summary>
    /// Shows <paramref name="items"/> for <paramref name="owner"/> at (<paramref name="screenX"/>, <paramref name="screenY"/>)
    /// and runs the chosen one; blocks while the menu is open. With <paramref name="opensUp"/> it opens above the point
    /// (from the controls bar at the bottom of the window, or the tray).
    /// </summary>
    public static void Show(HWND owner, IReadOnlyList<MenuItem> items, int screenX, int screenY, bool opensUp)
    {
        ArgumentNullException.ThrowIfNull(items);
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item.Title is null)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
                    continue;
                }
                var flags = MENU_ITEM_FLAGS.MF_STRING;
                if (item.IsChecked) flags |= MENU_ITEM_FLAGS.MF_CHECKED;
                if (!item.IsEnabled) flags |= MENU_ITEM_FLAGS.MF_GRAYED;
                // Ids start at 1: TrackPopupMenuEx returns 0 when nothing was chosen.
                fixed (char* title = item.Title.Replace("&", "&&", StringComparison.Ordinal)) PInvoke.AppendMenu(menu, flags, (nuint)(index + 1), title);
            }
            // A menu needs its owner in the foreground, or it stays open after a click elsewhere (tray menus).
            PInvoke.SetForegroundWindow(owner);
            var alignment = opensUp ? TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN : TRACK_POPUP_MENU_FLAGS.TPM_TOPALIGN;
            var chosen = PInvoke.TrackPopupMenuEx(menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | alignment), screenX, screenY, owner, null);
            if (chosen.Value > 0 && chosen.Value <= items.Count) items[chosen.Value - 1].Choose?.Invoke();
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }
}
