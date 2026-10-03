namespace Plugview.Core.Selection;

/// <summary>How a capture device is attached; decides whether plugview may pick it by itself.</summary>
public enum DeviceTransport
{
    /// <summary>Part of the PC: a laptop camera or microphone, even when wired over internal USB.</summary>
    BuiltIn,

    /// <summary>An external USB device.</summary>
    Usb,

    /// <summary>A software camera.</summary>
    Virtual,

    /// <summary>A phone camera (Phone Link).</summary>
    Wireless,

    /// <summary>A PCI Express card, and anything else wired.</summary>
    Other,
}

/// <summary>Framework-free description of a capture device, so selection rules stay testable.</summary>
/// <param name="UniqueId">Stable across replugs; remembered as the user's pick.</param>
/// <param name="Name">The name the system shows.</param>
/// <param name="ModelId">Shared by the video and audio halves of one card: on Windows, the device's <c>ContainerId</c>.</param>
/// <param name="Transport">How the device is attached.</param>
public sealed record DeviceInfo(string UniqueId, string Name, string ModelId, DeviceTransport Transport)
{
    internal bool Matches(string query) =>
        UniqueId == query || Name.Contains(query, StringComparison.InvariantCultureIgnoreCase);
}

/// <summary>Which capture device plays, and which audio input belongs to it.</summary>
public static class DeviceSelection
{
    /// <summary>The video devices plugview may show, best first: without a query only wired hardware, USB first.</summary>
    public static IReadOnlyList<DeviceInfo> PlayableVideoDevices(IEnumerable<DeviceInfo> candidates, string? query)
    {
        var devices = candidates.ToList();
        if (query is not null) return [.. devices.Where(device => device.Matches(query))];
        return [.. devices.Where(device => device.Transport == DeviceTransport.Usb), .. devices.Where(device => device.Transport == DeviceTransport.Other)];
    }

    /// <summary>The video device to show: the best of <see cref="PlayableVideoDevices"/>.</summary>
    public static DeviceInfo? SelectVideoDevice(IEnumerable<DeviceInfo> candidates, string? query) =>
        PlayableVideoDevices(candidates, query) is [var best, ..] ? best : null;

    /// <summary>The audio input that belongs to <paramref name="video"/>: same name, then same model ID, then the only USB input.</summary>
    public static DeviceInfo? SelectAudioDevice(IEnumerable<DeviceInfo> candidates, DeviceInfo video, string? query)
    {
        ArgumentNullException.ThrowIfNull(video);
        var devices = candidates.ToList();
        if (query is not null) return devices.FirstOrDefault(device => device.Matches(query));
        if (devices.FirstOrDefault(device => device.Name == video.Name) is { } sameName) return sameName;
        if (video.ModelId.Length > 0 && devices.FirstOrDefault(device => device.ModelId == video.ModelId) is { } sameModel) return sameModel;

        var usbInputs = devices.Where(device => device.Transport == DeviceTransport.Usb).ToList();
        return video.Transport == DeviceTransport.Usb && usbInputs.Count == 1 ? usbInputs[0] : null;
    }

    /// <summary>Puts the device the user picked last in front, keeping the rest in their order.</summary>
    public static IReadOnlyList<DeviceInfo> PreferredFirst(IEnumerable<DeviceInfo> devices, string? preferredId)
    {
        var list = devices.ToList();
        var preferred = list.FirstOrDefault(device => device.UniqueId == preferredId);
        if (preferred is null) return list;
        return [preferred, .. list.Where(device => device.UniqueId != preferredId)];
    }

    /// <summary>Whether the launcher plays on its own: one device, or the one the user picked before.</summary>
    public static bool ShouldAutoPlay(IReadOnlyCollection<DeviceInfo> devices, string? preferredId)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices.Count == 1 || devices.Any(device => device.UniqueId == preferredId);
    }
}
