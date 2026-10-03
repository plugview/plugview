using Plugview.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;

namespace Plugview.Windows.Video;

/// <summary>A frame layout the presenter can draw: packed 4:2:2 (YUY2) or 4:2:0 (NV12), with its color space.</summary>
/// <param name="Width">Pixels.</param>
/// <param name="Height">Pixels.</param>
/// <param name="Format"><c>DXGI_FORMAT_YUY2</c> or <c>DXGI_FORMAT_NV12</c>.</param>
/// <param name="IsFullRange">Full-range YCbCr (decoded MJPEG); otherwise video range (16–235).</param>
internal readonly record struct FrameFormat(uint Width, uint Height, DXGI_FORMAT Format, bool IsFullRange)
{
    /// <summary>BT.709 from 720 lines up, BT.601 below (SD), in the range the frame uses (K05).</summary>
    public DXGI_COLOR_SPACE_TYPE ColorSpace => (Height >= 720, IsFullRange) switch
    {
        (true, false) => DXGI_COLOR_SPACE_TYPE.DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709,
        (true, true) => DXGI_COLOR_SPACE_TYPE.DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P709,
        (false, false) => DXGI_COLOR_SPACE_TYPE.DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601,
        (false, true) => DXGI_COLOR_SPACE_TYPE.DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P601,
    };
}

/// <summary>One frame read back from the GPU as 8-bit BGRA rows, for a screenshot or the signal watch.</summary>
internal sealed record FrameGrab(uint Width, uint Height, byte[] Bgra)
{
    /// <summary>The brightest pixel's luma (BT.709, 0–255): how the signal watch tells a black picture (K01).</summary>
    public int MaxLuma()
    {
        var max = 0.0;
        for (var index = 0; index + 3 < Bgra.Length; index += 4)
        {
            var luma = (0.0722 * Bgra[index]) + (0.7152 * Bgra[index + 1]) + (0.2126 * Bgra[index + 2]);
            if (luma > max) max = luma;
        }
        return (int)Math.Round(max);
    }
}

/// <summary>
/// Draws capture frames into a window with the least delay Windows allows (K08-K10).
/// </summary>
/// <remarks>
/// One Direct3D 11 device serves both Media Foundation (hardware MJPEG decode writes straight into its textures) and
/// a flip-model swap chain with at most one frame queued. The GPU's video processor converts YUY2/NV12 to the screen,
/// scales with letterboxing, and applies the color space; frames in system memory are uploaded once. Frames are drawn
/// as they arrive, never on a timer, so the display shows the newest frame at its next refresh.
/// </remarks>
internal sealed unsafe class VideoPresenter : IDisposable
{
    private readonly Lock gate = new();
    private ComPtr<ID3D11Device> device;
    private ComPtr<ID3D11DeviceContext> context;
    private ComPtr<ID3D11VideoDevice> videoDevice;
    private ComPtr<ID3D11VideoContext1> videoContext;
    private ComPtr<IMFDXGIDeviceManager> deviceManager;
    private ComPtr<IDXGIFactory5> factory;
    private ComPtr<IDXGISwapChain2> swapChain;
    private ComPtr<ID3D11VideoProcessorEnumerator> processorEnumerator;
    private ComPtr<ID3D11VideoProcessor> processor;
    private ComPtr<ID3D11Texture2D> uploadTexture;
    private HANDLE frameLatencyWaitable;
    private FrameFormat processorInput;
    private (uint Width, uint Height) processorOutput;
    private (uint Width, uint Height) uploadSize;
    private DXGI_FORMAT uploadFormat;
    private (uint Width, uint Height) windowSize;
    private bool isTearingSupported;
    private bool expandsLimitedRange;
    private readonly List<(uint Width, uint Height, Action<FrameGrab?> Done)> grabRequests = [];

    /// <summary>
    /// Full-range colors: stretches the picture's levels (16–235 to 0–255) for cards that pass a console's limited
    /// range through as if it were full, which leaves blacks gray. Applied by the GPU's video processor (contrast and
    /// brightness), so the frames stay on the GPU; drivers without those filters leave the picture as it is.
    /// </summary>
    public bool ExpandsLimitedRange
    {
        get
        {
            lock (gate) return expandsLimitedRange;
        }
        set
        {
            lock (gate)
            {
                expandsLimitedRange = value;
                // Rebuilt with the new filters on the next frame.
                processor.Dispose();
            }
        }
    }

    /// <summary>
    /// Reads the next frame back at <paramref name="width"/>×<paramref name="height"/> (0×0: the frame's own size) and hands
    /// it to <paramref name="done"/> on the capture thread; <c>null</c> when it could not be read.
    /// </summary>
    public void RequestGrab(uint width, uint height, Action<FrameGrab?> done)
    {
        lock (gate) grabRequests.Add((width, height, done));
    }

    /// <summary>Shows black: the card stopped, so its last frame must not linger under the launcher or a notice.</summary>
    public void Clear()
    {
        lock (gate)
        {
            if (swapChain.IsNull) return;
            ID3D11Texture2D* rawBackBuffer;
            var textureId = ID3D11Texture2D.IID_Guid;
            swapChain.Get()->GetBuffer(0, &textureId, (void**)&rawBackBuffer);
            using var backBuffer = new ComPtr<ID3D11Texture2D>(rawBackBuffer);
            ID3D11RenderTargetView* rawView;
            device.Get()->CreateRenderTargetView((ID3D11Resource*)backBuffer.Get(), null, &rawView);
            using var view = new ComPtr<ID3D11RenderTargetView>(rawView);
            var black = stackalloc float[] { 0, 0, 0, 1 };
            context.Get()->ClearRenderTargetView(view.Get(), black);
            swapChain.Get()->Present(0, 0);
        }
    }

    public VideoPresenter()
    {
        CreateDevice();
    }

    /// <summary>The device manager Media Foundation uses, so decoded frames land in this device's textures.</summary>
    public IMFDXGIDeviceManager* DeviceManager => deviceManager.Get();

    /// <summary>Creates the swap chain for <paramref name="window"/>.</summary>
    public void Attach(HWND window, uint width, uint height)
    {
        lock (gate)
        {
            var flags = DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
            if (isTearingSupported) flags |= DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
            var description = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = width,
                Height = height,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 2,
                Scaling = DXGI_SCALING.DXGI_SCALING_NONE,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
                Flags = flags,
            };
            IDXGISwapChain1* created;
            factory.Get()->CreateSwapChainForHwnd((IUnknown*)device.Get(), window, &description, null, null, &created);
            using var swapChain1 = new ComPtr<IDXGISwapChain1>(created);
            swapChain = swapChain1.As<IDXGISwapChain2>();
            // One frame in flight: the swap chain never queues frames behind the newest one.
            swapChain.Get()->SetMaximumFrameLatency(1);
            frameLatencyWaitable = swapChain.Get()->GetFrameLatencyWaitableObject();
            windowSize = (width, height);
        }
    }

    /// <summary>Follows the window's client size.</summary>
    public void Resize(uint width, uint height)
    {
        lock (gate)
        {
            if (swapChain.IsNull || width == 0 || height == 0 || (width, height) == windowSize) return;
            var flags = DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
            if (isTearingSupported) flags |= DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
            swapChain.Get()->ResizeBuffers(0, width, height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, (uint)flags);
            windowSize = (width, height);
        }
    }

    /// <summary>Draws one captured frame and presents it at the next refresh.</summary>
    public void Present(IMFSample* sample, FrameFormat format)
    {
        lock (gate)
        {
            if (swapChain.IsNull) return;
            // Waits only if the previous frame is still queued, which keeps the queue at one frame.
            PInvoke.WaitForSingleObjectEx(frameLatencyWaitable, 100, true);

            IMFMediaBuffer* rawBuffer;
            sample->GetBufferByIndex(0, &rawBuffer);
            using var buffer = new ComPtr<IMFMediaBuffer>(rawBuffer);
            using var input = InputTexture(buffer, format, out var subresource);
            if (input.IsNull) return;

            EnsureProcessor(format);
            using var inputView = InputView(input.Get(), subresource);
            ID3D11Texture2D* rawBackBuffer;
            var textureId = ID3D11Texture2D.IID_Guid;
            swapChain.Get()->GetBuffer(0, &textureId, (void**)&rawBackBuffer);
            using var backBuffer = new ComPtr<ID3D11Texture2D>(rawBackBuffer);
            using var outputView = OutputView(backBuffer.Get());

            var stream = new D3D11_VIDEO_PROCESSOR_STREAM { Enable = true, pInputSurface = inputView.Get() };
            videoContext.Get()->VideoProcessorBlt(processor.Get(), outputView.Get(), 0, 1, &stream);
            swapChain.Get()->Present(1, 0).ThrowOnFailure();
            if (grabRequests.Count > 0) ServeGrabs(&stream, format);
        }
    }

    /// <summary>
    /// Draws the frame once more into a small texture per request and reads it back. Runs after the frame is presented,
    /// so a screenshot or the signal watch never delays the picture.
    /// </summary>
    private void ServeGrabs(D3D11_VIDEO_PROCESSOR_STREAM* stream, FrameFormat format)
    {
        var requests = grabRequests.ToArray();
        grabRequests.Clear();
        var video = videoContext.Get();
        try
        {
            foreach (var (requestedWidth, requestedHeight, done) in requests)
            {
                var (width, height) = requestedWidth == 0 ? (format.Width, format.Height) : (requestedWidth, requestedHeight);
                done(Grab(stream, width, height));
            }
        }
        finally
        {
            // Back to drawing into the window.
            var target = new RECT { right = (int)windowSize.Width, bottom = (int)windowSize.Height };
            video->VideoProcessorSetOutputTargetRect(processor.Get(), true, &target);
            var picture = Letterbox(format.Width, format.Height, windowSize.Width, windowSize.Height);
            video->VideoProcessorSetStreamDestRect(processor.Get(), 0, true, &picture);
        }
    }

    private FrameGrab? Grab(D3D11_VIDEO_PROCESSOR_STREAM* stream, uint width, uint height)
    {
        try
        {
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET,
            };
            ID3D11Texture2D* rawTarget;
            device.Get()->CreateTexture2D(&description, null, &rawTarget);
            using var target = new ComPtr<ID3D11Texture2D>(rawTarget);
            var video = videoContext.Get();
            var whole = new RECT { right = (int)width, bottom = (int)height };
            video->VideoProcessorSetOutputTargetRect(processor.Get(), true, &whole);
            video->VideoProcessorSetStreamDestRect(processor.Get(), 0, true, &whole);
            using (var view = OutputView(target.Get())) video->VideoProcessorBlt(processor.Get(), view.Get(), 0, 1, stream);

            description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            description.BindFlags = 0;
            description.CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            ID3D11Texture2D* rawStaging;
            device.Get()->CreateTexture2D(&description, null, &rawStaging);
            using var staging = new ComPtr<ID3D11Texture2D>(rawStaging);
            context.Get()->CopyResource((ID3D11Resource*)staging.Get(), (ID3D11Resource*)target.Get());
            D3D11_MAPPED_SUBRESOURCE mapped;
            context.Get()->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
            try
            {
                var rowBytes = (int)width * 4;
                var pixels = new byte[rowBytes * (int)height];
                for (var row = 0; row < height; row++)
                {
                    new ReadOnlySpan<byte>((byte*)mapped.pData + (row * mapped.RowPitch), rowBytes).CopyTo(pixels.AsSpan(row * rowBytes));
                }
                return new FrameGrab(width, height, pixels);
            }
            finally
            {
                context.Get()->Unmap((ID3D11Resource*)staging.Get(), 0);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Full-range colors through the video processor's contrast and brightness filters, when the driver has them:
    /// contrast ×255/219 around black, brightness −16 code values (the Mac's levels expansion, approximated in YCbCr).
    /// </summary>
    private void ApplyLevels()
    {
        var video = videoContext.Get();
        D3D11_VIDEO_PROCESSOR_CAPS caps;
        processorEnumerator.Get()->GetVideoProcessorCaps(&caps);
        var filters = (D3D11_VIDEO_PROCESSOR_FILTER_CAPS)caps.FilterCaps;
        foreach (var (filter, flag, value) in (ReadOnlySpan<(D3D11_VIDEO_PROCESSOR_FILTER, D3D11_VIDEO_PROCESSOR_FILTER_CAPS, float)>)
            [
                (D3D11_VIDEO_PROCESSOR_FILTER.D3D11_VIDEO_PROCESSOR_FILTER_CONTRAST, D3D11_VIDEO_PROCESSOR_FILTER_CAPS.D3D11_VIDEO_PROCESSOR_FILTER_CAPS_CONTRAST, 255f / 219f),
                (D3D11_VIDEO_PROCESSOR_FILTER.D3D11_VIDEO_PROCESSOR_FILTER_BRIGHTNESS, D3D11_VIDEO_PROCESSOR_FILTER_CAPS.D3D11_VIDEO_PROCESSOR_FILTER_CAPS_BRIGHTNESS, -16f),
            ])
        {
            if (!filters.HasFlag(flag)) continue;
            D3D11_VIDEO_PROCESSOR_FILTER_RANGE range;
            processorEnumerator.Get()->GetVideoProcessorFilterRange(filter, &range);
            if (range.Multiplier <= 0) continue;
            var level = (int)Math.Clamp(Math.Round(value / range.Multiplier), range.Minimum, range.Maximum);
            video->VideoProcessorSetStreamFilter(processor.Get(), 0, filter, true, level);
        }
    }

    /// <summary>The frame as a GPU texture: the decoder's own texture when it has one, else an upload of its bytes.</summary>
    private ComPtr<ID3D11Texture2D> InputTexture(ComPtr<IMFMediaBuffer> buffer, FrameFormat format, out uint subresource)
    {
        subresource = 0;
        using var dxgiBuffer = buffer.TryAs<IMFDXGIBuffer>();
        if (!dxgiBuffer.IsNull)
        {
            ID3D11Texture2D* texture;
            var textureId = ID3D11Texture2D.IID_Guid;
            dxgiBuffer.Get()->GetResource(&textureId, (void**)&texture);
            uint index;
            dxgiBuffer.Get()->GetSubresourceIndex(&index);
            subresource = index;
            return new ComPtr<ID3D11Texture2D>(texture);
        }

        EnsureUploadTexture(format);
        using var buffer2D = buffer.TryAs<IMF2DBuffer>();
        if (!buffer2D.IsNull)
        {
            byte* scanline;
            int pitch;
            buffer2D.Get()->Lock2D(&scanline, &pitch);
            try
            {
                context.Get()->UpdateSubresource((ID3D11Resource*)uploadTexture.Get(), 0, null, scanline, (uint)pitch, 0);
            }
            finally
            {
                buffer2D.Get()->Unlock2D();
            }
        }
        else
        {
            byte* data;
            uint maxLength, length;
            buffer.Get()->Lock(&data, &maxLength, &length);
            try
            {
                var pitch = format.Format == DXGI_FORMAT.DXGI_FORMAT_YUY2 ? format.Width * 2 : format.Width;
                context.Get()->UpdateSubresource((ID3D11Resource*)uploadTexture.Get(), 0, null, data, pitch, 0);
            }
            finally
            {
                buffer.Get()->Unlock();
            }
        }
        ((IUnknown*)uploadTexture.Get())->AddRef();
        return new ComPtr<ID3D11Texture2D>(uploadTexture.Get());
    }

    private void EnsureUploadTexture(FrameFormat format)
    {
        if (!uploadTexture.IsNull && uploadSize == (format.Width, format.Height) && uploadFormat == format.Format) return;
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = format.Width,
            Height = format.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format.Format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
        };
        ID3D11Texture2D* texture;
        device.Get()->CreateTexture2D(&description, null, &texture);
        uploadTexture.Reset(texture);
        uploadSize = (format.Width, format.Height);
        uploadFormat = format.Format;
    }

    /// <summary>(Re)creates the video processor when the frame or the window changes size or format.</summary>
    private void EnsureProcessor(FrameFormat format)
    {
        if (!processor.IsNull && processorInput == format && processorOutput == windowSize) return;
        processor.Dispose();
        var content = new D3D11_VIDEO_PROCESSOR_CONTENT_DESC
        {
            InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT.D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
            InputWidth = format.Width,
            InputHeight = format.Height,
            OutputWidth = windowSize.Width,
            OutputHeight = windowSize.Height,
            Usage = D3D11_VIDEO_USAGE.D3D11_VIDEO_USAGE_PLAYBACK_NORMAL,
        };
        ID3D11VideoProcessorEnumerator* enumerator;
        videoDevice.Get()->CreateVideoProcessorEnumerator(&content, &enumerator);
        processorEnumerator.Reset(enumerator);
        ID3D11VideoProcessor* created;
        videoDevice.Get()->CreateVideoProcessor(processorEnumerator.Get(), 0, &created);
        processor.Reset(created);

        var video = videoContext.Get();
        video->VideoProcessorSetStreamFrameFormat(processor.Get(), 0, D3D11_VIDEO_FRAME_FORMAT.D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        video->VideoProcessorSetStreamColorSpace1(processor.Get(), 0, format.ColorSpace);
        video->VideoProcessorSetOutputColorSpace1(processor.Get(), DXGI_COLOR_SPACE_TYPE.DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
        var black = new D3D11_VIDEO_COLOR();
        black.Anonymous.RGBA.A = 1;
        video->VideoProcessorSetOutputBackgroundColor(processor.Get(), false, &black);
        var target = new RECT { right = (int)windowSize.Width, bottom = (int)windowSize.Height };
        video->VideoProcessorSetOutputTargetRect(processor.Get(), true, &target);
        var picture = Letterbox(format.Width, format.Height, windowSize.Width, windowSize.Height);
        video->VideoProcessorSetStreamDestRect(processor.Get(), 0, true, &picture);
        if (expandsLimitedRange) ApplyLevels();

        processorInput = format;
        processorOutput = windowSize;
    }

    /// <summary>The largest rectangle with the video's shape, centered in the window (K11: never stretched).</summary>
    internal static RECT Letterbox(uint videoWidth, uint videoHeight, uint windowWidth, uint windowHeight)
    {
        var scale = Math.Min((double)windowWidth / videoWidth, (double)windowHeight / videoHeight);
        var width = (int)Math.Round(videoWidth * scale, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(videoHeight * scale, MidpointRounding.AwayFromZero);
        var left = ((int)windowWidth - width) / 2;
        var top = ((int)windowHeight - height) / 2;
        return new RECT { left = left, top = top, right = left + width, bottom = top + height };
    }

    private ComPtr<ID3D11VideoProcessorInputView> InputView(ID3D11Texture2D* texture, uint subresource)
    {
        var description = new D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC { ViewDimension = D3D11_VPIV_DIMENSION.D3D11_VPIV_DIMENSION_TEXTURE2D };
        description.Anonymous.Texture2D.ArraySlice = subresource;
        ID3D11VideoProcessorInputView* view;
        videoDevice.Get()->CreateVideoProcessorInputView((ID3D11Resource*)texture, processorEnumerator.Get(), &description, &view);
        return new ComPtr<ID3D11VideoProcessorInputView>(view);
    }

    private ComPtr<ID3D11VideoProcessorOutputView> OutputView(ID3D11Texture2D* backBuffer)
    {
        var description = new D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC { ViewDimension = D3D11_VPOV_DIMENSION.D3D11_VPOV_DIMENSION_TEXTURE2D };
        ID3D11VideoProcessorOutputView* view;
        videoDevice.Get()->CreateVideoProcessorOutputView((ID3D11Resource*)backBuffer, processorEnumerator.Get(), &description, &view);
        return new ComPtr<ID3D11VideoProcessorOutputView>(view);
    }

    private void CreateDevice()
    {
        ReadOnlySpan<D3D_FEATURE_LEVEL> levels =
        [
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
        ];
        ID3D11Device* createdDevice;
        ID3D11DeviceContext* createdContext;
        PInvoke.D3D11CreateDevice(
            null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            levels, PInvoke.D3D11_SDK_VERSION, &createdDevice, out _, &createdContext).ThrowOnFailure();
        device.Reset(createdDevice);
        context.Reset(createdContext);

        // Media Foundation decodes on its own threads with this device.
        using (var multithread = device.As<ID3D11Multithread>()) multithread.Get()->SetMultithreadProtected(true);
        videoDevice = device.As<ID3D11VideoDevice>();
        videoContext = context.As<ID3D11VideoContext1>();

        MediaFoundation.Start();
        IMFDXGIDeviceManager* manager;
        PInvoke.MFCreateDXGIDeviceManager(out var resetToken, &manager).ThrowOnFailure();
        deviceManager.Reset(manager);
        deviceManager.Get()->ResetDevice((IUnknown*)device.Get(), resetToken);

        PInvoke.CreateDXGIFactory2(0, out IDXGIFactory5* createdFactory).ThrowOnFailure();
        factory = new ComPtr<IDXGIFactory5>(createdFactory);
        BOOL allowsTearing;
        try
        {
            factory.Get()->CheckFeatureSupport(DXGI_FEATURE.DXGI_FEATURE_PRESENT_ALLOW_TEARING, &allowsTearing, (uint)sizeof(BOOL));
            isTearingSupported = allowsTearing;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            isTearingSupported = false;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            processor.Dispose();
            processorEnumerator.Dispose();
            uploadTexture.Dispose();
            swapChain.Dispose();
            factory.Dispose();
            deviceManager.Dispose();
            videoContext.Dispose();
            videoDevice.Dispose();
            context.Dispose();
            device.Dispose();
        }
    }
}
