using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Plugview.Core.Selection;

namespace Plugview.Windows.Interop;

/// <summary>Media Foundation start-up and the attribute reads plugview needs.</summary>
internal static unsafe class MediaFoundation
{
    private static int startCount;

    /// <summary>Starts Media Foundation once per process; every capture and listing calls it first.</summary>
    public static void Start()
    {
        if (Interlocked.Increment(ref startCount) == 1) PInvoke.MFStartup(PInvoke.MF_VERSION, PInvoke.MFSTARTUP_LITE).ThrowOnFailure();
    }

    /// <summary>Creates an attribute store with the given keys set.</summary>
    public static ComPtr<IMFAttributes> Attributes(uint capacity)
    {
        IMFAttributes* attributes;
        PInvoke.MFCreateAttributes(&attributes, capacity).ThrowOnFailure();
        return new ComPtr<IMFAttributes>(attributes);
    }

    public static void SetGuid(IMFAttributes* attributes, Guid key, Guid value) => attributes->SetGUID(&key, &value);

    public static void SetUInt32(IMFAttributes* attributes, Guid key, uint value) => attributes->SetUINT32(&key, value);

    public static void SetString(IMFAttributes* attributes, Guid key, string value)
    {
        fixed (char* text = value) attributes->SetString(&key, text);
    }

    public static Guid GetGuid(IMFMediaType* type, Guid key)
    {
        Guid value;
        type->GetGUID(&key, &value);
        return value;
    }

    /// <summary>A packed pair such as MF_MT_FRAME_SIZE (width, height) or MF_MT_FRAME_RATE (numerator, denominator).</summary>
    public static (uint High, uint Low) GetPair(IMFMediaType* type, Guid key)
    {
        ulong packed;
        type->GetUINT64(&key, &packed);
        return ((uint)(packed >> 32), (uint)packed);
    }

    /// <summary>A packed pair, or <c>null</c> when the type does not have it.</summary>
    public static (uint High, uint Low)? TryGetPair(IMFMediaType* type, Guid key)
    {
        try
        {
            return GetPair(type, key);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static void SetPair(IMFAttributes* attributes, Guid key, uint high, uint low) => attributes->SetUINT64(&key, ((ulong)high << 32) | low);

    /// <summary>The capture format plugview's mode choice understands, from a native media type.</summary>
    public static VideoMode Mode(IMFMediaType* type)
    {
        var (width, height) = GetPair(type, PInvoke.MF_MT_FRAME_SIZE);
        var (numerator, denominator) = GetPair(type, PInvoke.MF_MT_FRAME_RATE);
        var frameRate = denominator == 0 ? 0 : (double)numerator / denominator;
        return new VideoMode(width, height, frameRate, Encoding(GetGuid(type, PInvoke.MF_MT_SUBTYPE)));
    }

    /// <summary>
    /// How much work a subtype needs: the packed 4:2:2 and NV12 that capture cards send are drawn by the GPU's video
    /// processor as they are; RGB is converted; MJPEG, H.264 and HEVC are decoded.
    /// </summary>
    public static FrameEncoding Encoding(Guid subtype)
    {
        if (subtype == PInvoke.MFVideoFormat_YUY2 || subtype == PInvoke.MFVideoFormat_UYVY || subtype == PInvoke.MFVideoFormat_NV12) return FrameEncoding.Native;
        if (subtype == PInvoke.MFVideoFormat_MJPG || subtype == PInvoke.MFVideoFormat_H264 || subtype == PInvoke.MFVideoFormat_HEVC) return FrameEncoding.Compressed;
        return FrameEncoding.Converted;
    }

    /// <summary>Opens the video capture device behind <paramref name="symbolicLink"/> as a media source.</summary>
    public static ComPtr<IMFMediaSource> OpenDevice(string symbolicLink)
    {
        Start();
        using var attributes = Attributes(2);
        SetGuid(attributes.Get(), PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
        SetString(attributes.Get(), PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, symbolicLink);
        IMFMediaSource* source;
        PInvoke.MFCreateDeviceSource(attributes.Get(), &source).ThrowOnFailure();
        return new ComPtr<IMFMediaSource>(source);
    }

    /// <summary>Every native format of the first video stream, in the device's order.</summary>
    public static IReadOnlyList<VideoMode> NativeModes(IMFSourceReader* reader)
    {
        var modes = new List<VideoMode>();
        for (uint index = 0; ; index++)
        {
            IMFMediaType* type = null;
            var result = TryGetNativeMediaType(reader, index, &type);
            if (result == HRESULT.MF_E_NO_MORE_TYPES) return modes;
            result.ThrowOnFailure();
            using var owned = new ComPtr<IMFMediaType>(type);
            modes.Add(Mode(type));
        }
    }

    public static HRESULT TryGetNativeMediaType(IMFSourceReader* reader, uint index, IMFMediaType** type)
    {
        try
        {
            reader->GetNativeMediaType(FirstVideoStream, index, type);
            return HRESULT.S_OK;
        }
        catch (Exception error) when (error.HResult == HRESULT.MF_E_NO_MORE_TYPES.Value)
        {
            return HRESULT.MF_E_NO_MORE_TYPES;
        }
    }

    public const uint FirstVideoStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
}
