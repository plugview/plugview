using System.Diagnostics;
using Plugview.Core.Launcher;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Plugview.Windows.App;

/// <summary>What Windows says about one permission.</summary>
internal enum PermissionStatus
{
    Allowed,

    /// <summary>Windows will ask the first time the card opens (the packaged app, first run).</summary>
    WillPrompt,

    Denied,
}

/// <summary>
/// Camera and microphone privacy. The packaged (Store) app gets Windows' consent prompt and per-app switches; the
/// unpackaged download depends on "Let desktop apps access your camera", which only shows as an access-denied error
/// when the card opens (handled as <see cref="LauncherState.PermissionDenied"/> by the app).
/// </summary>
internal static class Permissions
{
    /// <summary>Running from an MSIX package (the Store app), which has an identity Windows' privacy settings list.</summary>
    public static bool IsPackaged { get; } = DetectPackage();

    public static PermissionStatus Status(PermissionKind kind)
    {
        if (!IsPackaged) return PermissionStatus.Allowed;
        try
        {
            return AppCapability.Create(Capability(kind)).CheckAccess() switch
            {
                AppCapabilityAccessStatus.Allowed => PermissionStatus.Allowed,
                AppCapabilityAccessStatus.UserPromptRequired => PermissionStatus.WillPrompt,
                AppCapabilityAccessStatus.DeniedByUser or AppCapabilityAccessStatus.DeniedBySystem => PermissionStatus.Denied,
                // Not declared: a packaging mistake, not the user's choice; let opening the card report it.
                _ => PermissionStatus.Allowed,
            };
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return PermissionStatus.Allowed;
        }
    }

    /// <summary>Opens the Settings page where the permission is turned on.</summary>
    public static void OpenSettings(PermissionKind kind) => OpenUri(kind switch
    {
        PermissionKind.Camera => "ms-settings:privacy-webcam",
        PermissionKind.Microphone => "ms-settings:privacy-microphone",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled permission"),
    });

    /// <summary>Opens a web or settings link with the user's default handler, or runs a program with arguments.</summary>
    public static void OpenUri(string uri, string? arguments = null)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Arguments = arguments ?? "" });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"plugview: cannot open {uri}: {error.Message}");
        }
    }

    private static string Capability(PermissionKind kind) => kind switch
    {
        PermissionKind.Camera => "webcam",
        PermissionKind.Microphone => "microphone",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled permission"),
    };

    private static bool DetectPackage()
    {
        try
        {
            return global::Windows.ApplicationModel.Package.Current is not null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // No package identity: the unpackaged download or the command.
            return false;
        }
    }
}
