using System.Text.RegularExpressions;
using Plugview.Core.Selection;
using Plugview.Windows.Interop;
using Windows.Devices.Enumeration;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace Plugview.Windows.Devices;

/// <summary>The capture devices and audio inputs Windows reports, described the way the spec classifies them.</summary>
/// <remarks>
/// <c>ModelId</c> is the device's <c>ContainerId</c>, shared by every function of one physical device, so a card's
/// video and audio pair exactly (spec: "How a port describes devices").
/// </remarks>
public static partial class DeviceCatalog
{
    private const string ContainerIdKey = "System.Devices.ContainerId";
    private const string InstanceIdKey = "System.Devices.DeviceInstanceId";

    /// <summary>The container Windows gives everything built into the PC itself.</summary>
    private static readonly Guid ComputerContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>Every enabled video capture device, as Windows lists them.</summary>
    public static async Task<IReadOnlyList<DeviceInfo>> VideoDevicesAsync()
    {
        var found = await DeviceInformation.FindAllAsync(DeviceInformation.GetAqsFilterFromDeviceClass(DeviceClass.VideoCapture), [ContainerIdKey, InstanceIdKey]);
        return [.. found.Where(device => device.IsEnabled).Select(device => new DeviceInfo(device.Id, device.Name, ContainerText(device), VideoTransport(device)))];
    }

    /// <summary>Every enabled audio input; its unique ID is the WASAPI endpoint ID.</summary>
    /// <param name="videoDevices">The video devices, so an input inside a card gets the card's transport.</param>
    public static async Task<IReadOnlyList<DeviceInfo>> AudioInputsAsync(IReadOnlyList<DeviceInfo> videoDevices)
    {
        ArgumentNullException.ThrowIfNull(videoDevices);
        var found = await DeviceInformation.FindAllAsync(DeviceInformation.GetAqsFilterFromDeviceClass(DeviceClass.AudioCapture), [ContainerIdKey]);
        return [.. found
            .Where(device => device.IsEnabled)
            .Select(device =>
            {
                var container = ContainerText(device);
                var transport = videoDevices.FirstOrDefault(video => video.ModelId.Length > 0 && video.ModelId == container)?.Transport
                    ?? (Container(device) == ComputerContainer ? DeviceTransport.BuiltIn : DeviceTransport.Other);
                return new DeviceInfo(EndpointId(device.Id), device.Name, container, transport);
            })];
    }

    /// <summary>Every native mode of a video device, opening it briefly; empty when it cannot be opened (e.g. in use).</summary>
    public static IReadOnlyList<VideoMode> Modes(DeviceInfo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        try
        {
            return ReadModes(video.UniqueId);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static unsafe IReadOnlyList<VideoMode> ReadModes(string symbolicLink)
    {
        using var source = MediaFoundation.OpenDevice(symbolicLink);
        try
        {
            IMFSourceReader* reader;
            PInvoke.MFCreateSourceReaderFromMediaSource(source.Get(), null, &reader).ThrowOnFailure();
            using var ownedReader = new ComPtr<IMFSourceReader>(reader);
            return MediaFoundation.NativeModes(reader);
        }
        finally
        {
            source.Get()->Shutdown();
        }
    }

    /// <summary>Built-in when inside the PC; otherwise from how Windows enumerated it (USB, PCI, or software).</summary>
    private static DeviceTransport VideoTransport(DeviceInformation device)
    {
        if (Container(device) == ComputerContainer || device.EnclosureLocation is { Panel: not Panel.Unknown }) return DeviceTransport.BuiltIn;
        var instanceId = device.Properties.TryGetValue(InstanceIdKey, out var value) ? value as string ?? "" : "";
        if (instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) return DeviceTransport.Usb;
        if (instanceId.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase) || instanceId.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceTransport.Virtual;
        }
        return DeviceTransport.Other;
    }

    private static Guid? Container(DeviceInformation device) =>
        device.Properties.TryGetValue(ContainerIdKey, out var value) && value is Guid container ? container : null;

    private static string ContainerText(DeviceInformation device) => Container(device)?.ToString("B") ?? "";

    /// <summary>The WASAPI endpoint ID, e.g. <c>{0.0.1.00000000}.{guid}</c>, inside an audio interface path.</summary>
    private static string EndpointId(string interfaceId) => EndpointIdPattern().Match(interfaceId) is { Success: true } match ? match.Value : interfaceId;

    [GeneratedRegex(@"\{0\.0\.[01]\.[0-9A-Fa-f]+\}\.\{[0-9A-Fa-f-]+\}")]
    private static partial Regex EndpointIdPattern();
}
