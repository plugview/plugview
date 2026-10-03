using Windows.Win32.Foundation;

namespace Plugview.Windows.Playback;

/// <summary>Turns Media Foundation failures into sentences with an action (K16, K30, known-issues.md).</summary>
internal static class CaptureErrors
{
    public static string Describe(Exception error, string deviceName) => (uint)error.HResult switch
    {
        // MF_E_HW_MFT_FAILED_START_STREAMING, MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED
        0xC00D3704 or 0xC00D3EA3 => $"{deviceName} is open in another app (OBS, Discord, Camera). Close it and start plugview again.",
        // MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED
        0xC00D3EA2 => $"{deviceName} was disconnected.",
        // E_ACCESSDENIED: camera privacy is off for desktop apps or for plugview
        0x80070005 => "Windows blocks camera access. Turn on Settings › Privacy & security › Camera › Let desktop apps access your camera.",
        _ => $"{deviceName}: {error.Message}",
    };

    /// <summary>Whether the failure is the card being used elsewhere, which a later retry may fix.</summary>
    public static bool IsInUse(Exception error) => (uint)error.HResult is 0xC00D3704 or 0xC00D3EA3 || error.HResult == HRESULT.AUDCLNT_E_DEVICE_IN_USE.Value;
}
