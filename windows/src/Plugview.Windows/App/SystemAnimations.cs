using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Plugview.Windows.App;

/// <summary>Windows' "Animation effects" switch (Settings › Accessibility › Visual effects), plugview's Reduce Motion.</summary>
internal static class SystemAnimations
{
    /// <summary>False when the user turned animations off: every artwork rests on its final frame and nothing fades.</summary>
    public static unsafe bool AreEnabled
    {
        get
        {
            BOOL isOn = true;
            return !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &isOn, 0) || isOn;
        }
    }
}
