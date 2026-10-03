using Plugview.Core.Selection;
using Plugview.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;

namespace Plugview.Windows.Video;

/// <summary>When a frame reached plugview, for <c>--measure</c> and the signal watch.</summary>
/// <param name="CaptureToAppMilliseconds">From the driver's capture timestamp to this process.</param>
/// <param name="CaptureTime100ns">The driver's timestamp, for frame intervals.</param>
internal readonly record struct FrameTiming(double CaptureToAppMilliseconds, long CaptureTime100ns);

/// <summary>Reads one capture device on its own thread and hands every frame to the presenter as it arrives.</summary>
/// <remarks>
/// The reader shares the presenter's Direct3D device (hardware MJPEG decode lands in its textures) and runs with
/// <c>MF_LOW_LATENCY</c>. One read is outstanding at a time and each frame is drawn before the next is read, so frames
/// never queue in plugview (K09). A format change from the card (K03) is picked up from the read flags.
/// </remarks>
internal sealed unsafe class VideoCapture : IDisposable
{
    private readonly VideoPresenter presenter;
    private ComPtr<IMFMediaSource> source;
    private ComPtr<IMFSourceReader> reader;
    private FrameFormat format;
    private readonly bool isNativeCompressed;
    private Thread? thread;
    private volatile bool isStopping;

    /// <summary>Every frame's timing, on the capture thread.</summary>
    public event Action<FrameTiming>? FrameArrived;

    /// <summary>The capture stopped by itself (unplugged, or a device error), on the capture thread.</summary>
    public event Action<Exception>? Stopped;

    /// <summary>The mode the device runs in.</summary>
    public VideoMode Mode { get; }

    /// <summary>Opens <paramref name="video"/> in the user's mode, or the best one (spec: <c>modeIndex</c>).</summary>
    /// <exception cref="InvalidOperationException">The device lists no video formats.</exception>
    public VideoCapture(DeviceInfo video, ModeChoice? choice, VideoPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(video);
        this.presenter = presenter;
        source = MediaFoundation.OpenDevice(video.UniqueId);
        try
        {
            reader = CreateReader(source.Get(), presenter.DeviceManager);
            var modes = MediaFoundation.NativeModes(reader.Get());
            var index = VideoModes.ModeIndex(modes, choice) ?? throw new InvalidOperationException($"{video.Name} reports no video formats");
            Mode = modes[index];
            isNativeCompressed = SelectMode(reader.Get(), (uint)index);
            format = CurrentFormat();
        }
        catch
        {
            source.Get()->Shutdown();
            reader.Dispose();
            source.Dispose();
            throw;
        }
    }

    public void Start()
    {
        thread = new Thread(ReadFrames) { Name = "plugview capture", IsBackground = true, Priority = ThreadPriority.Highest };
        thread.Start();
    }

    private static ComPtr<IMFSourceReader> CreateReader(IMFMediaSource* mediaSource, IMFDXGIDeviceManager* deviceManager)
    {
        using var attributes = MediaFoundation.Attributes(3);
        var managerKey = PInvoke.MF_SOURCE_READER_D3D_MANAGER;
        attributes.Get()->SetUnknown(&managerKey, (IUnknown*)deviceManager);
        MediaFoundation.SetUInt32(attributes.Get(), PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
        MediaFoundation.SetUInt32(attributes.Get(), PInvoke.MF_LOW_LATENCY, 1);
        IMFSourceReader* created;
        PInvoke.MFCreateSourceReaderFromMediaSource(mediaSource, attributes.Get(), &created).ThrowOnFailure();
        return new ComPtr<IMFSourceReader>(created);
    }

    /// <summary>Selects the native type; anything but YUY2/NV12 is asked for as NV12 so the reader adds a decoder.</summary>
    /// <returns>Whether the card sends a compressed format.</returns>
    private static bool SelectMode(IMFSourceReader* reader, uint index)
    {
        IMFMediaType* rawNative;
        reader->GetNativeMediaType(MediaFoundation.FirstVideoStream, index, &rawNative);
        using var native = new ComPtr<IMFMediaType>(rawNative);
        var subtype = MediaFoundation.GetGuid(native.Get(), PInvoke.MF_MT_SUBTYPE);
        if (subtype == PInvoke.MFVideoFormat_YUY2 || subtype == PInvoke.MFVideoFormat_NV12)
        {
            reader->SetCurrentMediaType(MediaFoundation.FirstVideoStream, null, native.Get());
            return false;
        }

        // The native type first, so the device runs in the chosen mode; then NV12 out of the same size and rate, so the
        // reader adds a decoder (in hardware when it can). Only the attributes that describe the picture are copied:
        // a compressed type's sample size or stride would make some decoders refuse it.
        reader->SetCurrentMediaType(MediaFoundation.FirstVideoStream, null, native.Get());
        IMFMediaType* rawOutput;
        PInvoke.MFCreateMediaType(&rawOutput).ThrowOnFailure();
        using var output = new ComPtr<IMFMediaType>(rawOutput);
        var outputAttributes = (IMFAttributes*)output.Get();
        MediaFoundation.SetGuid(outputAttributes, PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
        MediaFoundation.SetGuid(outputAttributes, PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_NV12);
        foreach (var key in (ReadOnlySpan<Guid>)[PInvoke.MF_MT_FRAME_SIZE, PInvoke.MF_MT_FRAME_RATE, PInvoke.MF_MT_PIXEL_ASPECT_RATIO])
        {
            var (high, low) = MediaFoundation.TryGetPair(native.Get(), key) ?? (0u, 0u);
            if (high != 0 || low != 0) MediaFoundation.SetPair(outputAttributes, key, high, low);
        }
        reader->SetCurrentMediaType(MediaFoundation.FirstVideoStream, null, output.Get());
        return MediaFoundation.Encoding(subtype) == FrameEncoding.Compressed;
    }

    /// <summary>The frame layout the reader delivers now.</summary>
    private FrameFormat CurrentFormat()
    {
        IMFMediaType* rawCurrent;
        reader.Get()->GetCurrentMediaType(MediaFoundation.FirstVideoStream, &rawCurrent);
        using var current = new ComPtr<IMFMediaType>(rawCurrent);
        var (width, height) = MediaFoundation.GetPair(current.Get(), PInvoke.MF_MT_FRAME_SIZE);
        var subtype = MediaFoundation.GetGuid(current.Get(), PInvoke.MF_MT_SUBTYPE);
        var dxgiFormat = subtype == PInvoke.MFVideoFormat_YUY2 ? DXGI_FORMAT.DXGI_FORMAT_YUY2 : DXGI_FORMAT.DXGI_FORMAT_NV12;
        // JPEG is full range by definition; raw formats are video range unless the type says otherwise.
        var isFullRange = isNativeCompressed || NominalRange(current.Get()) == MFNominalRange.MFNominalRange_0_255;
        return new FrameFormat(width, height, dxgiFormat, isFullRange);
    }

    private static MFNominalRange NominalRange(IMFMediaType* type)
    {
        var key = PInvoke.MF_MT_VIDEO_NOMINAL_RANGE;
        uint value;
        try
        {
            type->GetUINT32(&key, &value);
            return (MFNominalRange)value;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return MFNominalRange.MFNominalRange_Unknown;
        }
    }

    private void ReadFrames()
    {
        PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED).ThrowOnFailure();
        try
        {
            while (!isStopping) ReadOneFrame();
        }
        catch (Exception error) when (!isStopping)
        {
            Stopped?.Invoke(error);
        }
        catch (Exception) when (isStopping)
        {
            // Shutting the source down ends the blocked read with an error; that is how stopping works.
        }
    }

    private void ReadOneFrame()
    {
        uint flags;
        long timestamp;
        IMFSample* rawSample = null;
        reader.Get()->ReadSample(MediaFoundation.FirstVideoStream, 0, null, &flags, &timestamp, &rawSample);
        using var sample = new ComPtr<IMFSample>(rawSample);

        var readerFlags = (MF_SOURCE_READER_FLAG)flags;
        if (readerFlags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) || readerFlags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
        {
            throw new InvalidOperationException($"the capture stream ended (flags 0x{flags:X})");
        }
        if (readerFlags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) || readerFlags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED))
        {
            format = CurrentFormat();
        }
        if (sample.IsNull) return;

        var latency = CaptureToAppMilliseconds(sample.Get(), timestamp);
        presenter.Present(sample.Get(), format);
        FrameArrived?.Invoke(new FrameTiming(latency, timestamp));
    }

    /// <summary>From the driver's QPC timestamp when the sample has one, else from the presentation time.</summary>
    private static double CaptureToAppMilliseconds(IMFSample* sample, long presentationTime)
    {
        var key = PInvoke.MFSampleExtension_DeviceTimestamp;
        ulong deviceTime;
        long captureTime = presentationTime;
        try
        {
            ((IMFAttributes*)sample)->GetUINT64(&key, &deviceTime);
            captureTime = (long)deviceTime;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // No device timestamp: the presentation time is on the same 100 ns system clock.
        }
        return (Clock.Now100ns() - captureTime) / 10_000.0;
    }

    public void Dispose()
    {
        isStopping = true;
        if (!source.IsNull) source.Get()->Shutdown();
        thread?.Join(TimeSpan.FromSeconds(2));
        reader.Dispose();
        source.Dispose();
    }
}
