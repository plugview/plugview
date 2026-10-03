using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;

namespace Plugview.Windows.Graphics;

/// <summary>
/// Everything plugview draws over the video (launcher screens, controls, window buttons, toasts): one Direct2D surface
/// in a DirectComposition visual placed above the window's own swap chain.
/// </summary>
/// <remarks>
/// The video keeps its flip-model swap chain on the window (the zero-copy path); this visual sits on top as a
/// transparent layer. With nothing drawn (controls hidden while playing) the layer is empty and Windows can flip the
/// video straight to the display in full screen. The overlay has its own small Direct3D device, so drawing never waits
/// on the video thread's device.
/// </remarks>
internal sealed unsafe class Overlay : IDisposable
{
    private ComPtr<ID3D11Device> device;
    private ComPtr<ID2D1Factory1> factory;
    private ComPtr<ID2D1Device> drawingDevice;
    private ComPtr<IDCompositionDevice> composition;
    private ComPtr<IDCompositionTarget> target;
    private ComPtr<IDCompositionVisual> visual;
    private ComPtr<IDCompositionSurface> surface;
    private (uint Width, uint Height) surfaceSize;
    private bool isVisible = true;

    public Overlay(HWND window)
    {
        CreateDevices();
        Fonts = new FontSet();
        IDCompositionTarget* createdTarget;
        // topmost: above the window's own content, which is the video's swap chain.
        composition.Get()->CreateTargetForHwnd(window, true, &createdTarget);
        target.Reset(createdTarget);
        IDCompositionVisual* createdVisual;
        composition.Get()->CreateVisual(&createdVisual);
        visual.Reset(createdVisual);
        target.Get()->SetRoot(visual.Get());
    }

    public FontSet Fonts { get; }

    /// <summary>The DPI drawing happens at; coordinates passed to <see cref="Draw"/> are in DIPs (96 per inch).</summary>
    public float Dpi { get; set; } = 96;

    /// <summary>
    /// Redraws the whole overlay at <paramref name="width"/>×<paramref name="height"/> pixels; <paramref name="draw"/> gets a
    /// cleared, transparent canvas. A <c>null</c> drawing empties the layer (nothing over the video).
    /// </summary>
    public void Draw(uint width, uint height, Action<Canvas>? draw)
    {
        if (draw is null || width == 0 || height == 0)
        {
            if (isVisible)
            {
                visual.Get()->SetContent(null);
                composition.Get()->Commit();
                isVisible = false;
            }
            return;
        }
        EnsureSurface(width, height);
        ID2D1DeviceContext* context;
        var contextId = ID2D1DeviceContext.IID_Guid;
        System.Drawing.Point offset;
        surface.Get()->BeginDraw(null, &contextId, (void**)&context, &offset);
        using var contextReference = new ComPtr<ID2D1DeviceContext>(context);
        try
        {
            context->SetDpi(Dpi, Dpi);
            var scale = Dpi / 96f;
            // The surface may live inside a larger atlas: draw at the offset it was given.
            var origin = new D2D_MATRIX_3X2_F { _11 = 1, _22 = 1, _31 = offset.X / scale, _32 = offset.Y / scale };
            context->SetTransform(&origin);
            var clear = default(D2D1_COLOR_F);
            context->Clear(&clear);
            using var canvas = new Canvas(context, factory.Get(), Fonts, origin, width / scale, height / scale);
            draw(canvas);
        }
        finally
        {
            surface.Get()->EndDraw();
        }
        if (!isVisible)
        {
            visual.Get()->SetContent((IUnknown*)surface.Get());
            isVisible = true;
        }
        composition.Get()->Commit();
    }

    private void EnsureSurface(uint width, uint height)
    {
        if (!surface.IsNull && surfaceSize == (width, height)) return;
        IDCompositionSurface* created;
        composition.Get()->CreateSurface(width, height, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED, &created);
        surface.Reset(created);
        surfaceSize = (width, height);
        visual.Get()->SetContent((IUnknown*)surface.Get());
        isVisible = true;
    }

    private void CreateDevices()
    {
        ID3D11Device* createdDevice;
        var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        var result = PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default, flags, null, PInvoke.D3D11_SDK_VERSION, &createdDevice, out _, null);
        // No GPU driver (a VM, a remote session): WARP draws the overlay in software, which is plenty for it.
        if (result.Failed) PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, default, flags, null, PInvoke.D3D11_SDK_VERSION, &createdDevice, out _, null).ThrowOnFailure();
        device.Reset(createdDevice);

        var factoryId = ID2D1Factory1.IID_Guid;
        void* createdFactory;
        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, &factoryId, null, &createdFactory).ThrowOnFailure();
        factory.Reset((ID2D1Factory1*)createdFactory);

        using var dxgiDevice = device.As<IDXGIDevice>();
        ID2D1Device* createdDrawingDevice;
        factory.Get()->CreateDevice(dxgiDevice.Get(), &createdDrawingDevice);
        drawingDevice.Reset(createdDrawingDevice);

        var compositionId = IDCompositionDevice.IID_Guid;
        void* createdComposition;
        PInvoke.DCompositionCreateDevice2((IUnknown*)drawingDevice.Get(), &compositionId, &createdComposition).ThrowOnFailure();
        composition.Reset((IDCompositionDevice*)createdComposition);
    }

    public void Dispose()
    {
        Fonts.Dispose();
        surface.Dispose();
        visual.Dispose();
        target.Dispose();
        composition.Dispose();
        drawingDevice.Dispose();
        factory.Dispose();
        device.Dispose();
    }
}
