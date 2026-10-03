using Plugview.Core.Selection;

namespace Plugview.Core.Launcher;

/// <summary>A permission plugview needs from Windows.</summary>
public enum PermissionKind
{
    /// <summary>Shows the card's video.</summary>
    Camera,

    /// <summary>Plays the console's sound (never the PC's own microphone).</summary>
    Microphone,
}

/// <summary>Why a device that was found could not be played.</summary>
/// <param name="DeviceName">The device's name.</param>
/// <param name="Reason">What went wrong, in words the user can act on.</param>
public sealed record CaptureFailure(string DeviceName, string Reason);

/// <summary>What the launcher window shows.</summary>
public abstract record LauncherState
{
    private LauncherState() { }

    /// <summary>Windows is asking for a permission.</summary>
    public sealed record PermissionNeeded(PermissionKind Kind) : LauncherState;

    /// <summary>The user refused a permission plugview cannot work without.</summary>
    public sealed record PermissionDenied(PermissionKind Kind) : LauncherState;

    /// <summary>No playable device is connected.</summary>
    public sealed record WaitingForSignal : LauncherState;

    /// <summary>Playable devices are connected, best first, and none is playing yet.</summary>
    public sealed record Ready(IReadOnlyList<DeviceInfo> Devices) : LauncherState
    {
        /// <inheritdoc/>
        public bool Equals(Ready? other) => other is not null && Devices.SequenceEqual(other.Devices);

        /// <inheritdoc/>
        public override int GetHashCode() => Devices.Count;
    }

    /// <summary>The device whose video is on screen.</summary>
    public sealed record Playing(DeviceInfo Device) : LauncherState;

    /// <summary>Starting a device failed; the next device change tries again.</summary>
    public sealed record Failed(CaptureFailure Failure) : LauncherState;
}

/// <summary>Something that happened, reported by the app and the device watcher.</summary>
public abstract record LauncherEvent
{
    private LauncherEvent() { }

    /// <summary>Windows is about to ask for a permission.</summary>
    public sealed record PermissionRequested(PermissionKind Kind) : LauncherEvent;

    /// <summary>The user refused a permission.</summary>
    public sealed record PermissionDenied(PermissionKind Kind) : LauncherEvent;

    /// <summary>Every permission plugview needs to play is granted.</summary>
    public sealed record PermissionsGranted : LauncherEvent;

    /// <summary>The playable video devices right now, best first.</summary>
    public sealed record DevicesChanged(IReadOnlyList<DeviceInfo> Devices) : LauncherEvent;

    /// <summary>Video from <paramref name="Device"/> is on screen.</summary>
    public sealed record PlaybackStarted(DeviceInfo Device) : LauncherEvent;

    /// <summary>Starting a device failed.</summary>
    public sealed record PlaybackFailed(CaptureFailure Failure) : LauncherEvent;

    /// <summary>The playing device stopped; a <see cref="DevicesChanged"/> follows.</summary>
    public sealed record PlaybackStopped : LauncherEvent;
}

/// <summary>The launcher state machine.</summary>
public static class LauncherTransitions
{
    /// <summary>The state that follows <paramref name="state"/> once <paramref name="launcherEvent"/> happens.</summary>
    public static LauncherState Next(LauncherState state, LauncherEvent launcherEvent) => launcherEvent switch
    {
        LauncherEvent.PermissionRequested requested => new LauncherState.PermissionNeeded(requested.Kind),
        LauncherEvent.PermissionDenied denied => new LauncherState.PermissionDenied(denied.Kind),
        LauncherEvent.PermissionsGranted or LauncherEvent.PlaybackStopped => new LauncherState.WaitingForSignal(),
        LauncherEvent.DevicesChanged changed => AfterDevicesChanged(state, changed.Devices),
        LauncherEvent.PlaybackStarted started => new LauncherState.Playing(started.Device),
        LauncherEvent.PlaybackFailed failed => new LauncherState.Failed(failed.Failure),
        _ => throw new ArgumentOutOfRangeException(nameof(launcherEvent), launcherEvent, "Unhandled launcher event"),
    };

    private static LauncherState AfterDevicesChanged(LauncherState state, IReadOnlyList<DeviceInfo> devices) => state switch
    {
        LauncherState.PermissionNeeded or LauncherState.PermissionDenied => state,
        LauncherState.Playing playing when devices.Contains(playing.Device) => state,
        LauncherState.Playing or LauncherState.WaitingForSignal or LauncherState.Ready or LauncherState.Failed =>
            devices.Count == 0 ? new LauncherState.WaitingForSignal() : new LauncherState.Ready(devices),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unhandled launcher state"),
    };
}
