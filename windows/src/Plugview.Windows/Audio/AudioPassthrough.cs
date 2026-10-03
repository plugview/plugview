using System.Text.RegularExpressions;
using Plugview.Core.Audio;
using Plugview.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using Windows.Win32.Security;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Plugview.Windows.Audio;

/// <summary>Plays the card's audio input on the default output, in shared mode, with the delay held small (K22-K25).</summary>
/// <remarks>
/// One real-time thread (MMCSS "Pro Audio") waits on both endpoints' events, so the buffer between them needs no lock.
/// Capture runs in RAW mode, without Windows' microphone processing. Windows converts a normal card to the output's
/// rate; an MS2109 is read as declared (96 kHz mono), reinterpreted as 48 kHz stereo, then resampled here.
/// <see cref="AudioDriftController"/> keeps the buffer at <see cref="TargetMilliseconds"/> against clock drift.
/// </remarks>
internal sealed unsafe partial class AudioPassthrough : IDisposable
{
    private const double TargetMilliseconds = 20;
    private const double CapMilliseconds = 60;
    private const int OutputChannelsUsed = 2;

    private ComPtr<IAudioClient3> captureClient;
    private ComPtr<IAudioClient3> renderClient;
    private ComPtr<IAudioCaptureClient> capture;
    private ComPtr<IAudioRenderClient> render;
    private readonly HANDLE captureEvent;
    private readonly HANDLE renderEvent;
    private readonly int captureRate;
    private readonly int captureChannels;
    private readonly int renderRate;
    private readonly int renderChannels;
    private readonly uint renderBufferFrames;
    private readonly bool isMs2109;
    private readonly AudioDriftController drift;
    private readonly float[] ring;
    private long ringRead;
    private long ringWrite;
    private double readFraction;
    /// <summary>Set after an underrun: play silence until the buffer holds this many frames again.</summary>
    private int? refillUntilFrames;
    /// <summary>Room for one MS2109 packet read as stereo, so the audio thread never allocates.</summary>
    private readonly float[] quirkScratch;
    private long lastRenderTicks;
    private Thread? thread;
    private volatile bool isStopping;
    private volatile float volume = 1;

    /// <summary>The card's name, for status lines.</summary>
    public string InputName { get; }

    /// <summary>The output's name and whether it is Bluetooth, for <c>--measure</c>.</summary>
    public string OutputName { get; }

    public bool IsOutputBluetooth { get; }

    /// <summary>What the output adds: its stream latency plus one engine period.</summary>
    public double OutputLatencyMilliseconds { get; }

    /// <summary>The loudest recent card sample, in dB (about -120 for digital silence), for the signal watch.</summary>
    public float LevelDecibels { get; private set; } = -120;

    /// <summary>Capture-to-app latency of each card packet, from its QPC timestamp, for <c>--measure</c>.</summary>
    public event Action<double>? PacketArrived;

    /// <param name="inputEndpointId">The card's WASAPI capture endpoint (<c>DeviceInfo.UniqueId</c> of the audio input).</param>
    /// <param name="videoSymbolicLink">The card's video interface path, which names its USB IDs for quirk detection.</param>
    public AudioPassthrough(string inputEndpointId, string videoSymbolicLink)
    {
        PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
        using var enumerator = CreateEnumerator();

        using var inputDevice = GetDevice(enumerator.Get(), inputEndpointId);
        using var outputDevice = DefaultOutput(enumerator.Get());
        InputName = FriendlyName(inputDevice.Get());
        OutputName = FriendlyName(outputDevice.Get());
        IsOutputBluetooth = EnumeratorName(outputDevice.Get()).StartsWith("BTH", StringComparison.OrdinalIgnoreCase);

        renderClient = Activate(outputDevice.Get());
        var renderFormat = MixFormat(renderClient.Get());
        try
        {
            renderRate = (int)renderFormat->nSamplesPerSec;
            renderChannels = renderFormat->nChannels;
            renderBufferFrames = InitializeRender(renderFormat, out var periodFrames);
            OutputLatencyMilliseconds = StreamLatencyMilliseconds(renderClient.Get()) + (periodFrames * 1000.0 / renderRate);
        }
        finally
        {
            PInvoke.CoTaskMemFree(renderFormat);
        }

        captureClient = Activate(inputDevice.Get());
        SetRawMode(captureClient.Get());
        var declared = MixFormat(captureClient.Get());
        try
        {
            var (vendorId, productId) = UsbIds(videoSymbolicLink);
            // The reinterpretation reads 32-bit floats, the shared-mode mix format; anything else takes the normal path.
            isMs2109 = IsFloat32(declared)
                && CardAudioQuirks.Detect(vendorId, productId, (int)declared->nSamplesPerSec, declared->nChannels) == CardAudioQuirk.Ms2109InterleavedStereo;
            if (isMs2109)
            {
                // Read exactly what the card declares; the reinterpretation below needs its samples untouched.
                InitializeCapture(declared, 0);
                captureRate = (int)declared->nSamplesPerSec / 2;
                captureChannels = 2;
            }
            else
            {
                // Stereo float at the output's rate: Windows converts with its own good resampler.
                var stereo = FloatFormat(renderRate, 2);
                InitializeCapture((WAVEFORMATEX*)&stereo, PInvoke.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | PInvoke.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY);
                captureRate = renderRate;
                captureChannels = 2;
            }
        }
        finally
        {
            PInvoke.CoTaskMemFree(declared);
        }

        captureEvent = PInvoke.CreateEvent((SECURITY_ATTRIBUTES*)null, false, false, default(PCWSTR));
        renderEvent = PInvoke.CreateEvent((SECURITY_ATTRIBUTES*)null, false, false, default(PCWSTR));
        captureClient.Get()->SetEventHandle(captureEvent);
        renderClient.Get()->SetEventHandle(renderEvent);
        capture = Service<IAudioCaptureClient>(captureClient.Get());
        render = Service<IAudioRenderClient>(renderClient.Get());

        drift = new AudioDriftController(captureRate, TargetMilliseconds, CapMilliseconds);
        quirkScratch = new float[isMs2109 ? captureRate * 2 : 0];
        ring = new float[NextPowerOfTwo(captureRate / 2) * captureChannels];
    }

    /// <summary>0 to 1; 0 mutes.</summary>
    public float Volume
    {
        get => volume;
        set => volume = Math.Clamp(value, 0, 1);
    }

    public void Start()
    {
        thread = new Thread(Run) { Name = "plugview audio", IsBackground = true, Priority = ThreadPriority.Highest };
        thread.Start();
    }

    private void Run()
    {
        PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
        uint taskIndex = 0;
        using var realTime = PInvoke.AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
        captureClient.Get()->Start();
        renderClient.Get()->Start();
        lastRenderTicks = Clock.Now100ns();
        ReadOnlySpan<HANDLE> events = [captureEvent, renderEvent];
        while (!isStopping)
        {
            var woke = PInvoke.WaitForMultipleObjects(events, false, 200);
            if (woke == WAIT_EVENT.WAIT_OBJECT_0) DrainCapture();
            else if (woke == (WAIT_EVENT)((uint)WAIT_EVENT.WAIT_OBJECT_0 + 1)) FillRender();
        }
        captureClient.Get()->Stop();
        renderClient.Get()->Stop();
    }

    private void DrainCapture()
    {
        uint packet;
        capture.Get()->GetNextPacketSize(&packet);
        while (packet > 0)
        {
            byte* data;
            uint frames, flags;
            ulong qpcPosition;
            capture.Get()->GetBuffer(&data, &frames, &flags, null, &qpcPosition);
            var isSilent = (flags & (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
            var declaredChannels = isMs2109 ? 1 : captureChannels;
            var samples = new ReadOnlySpan<float>(data, (int)frames * declaredChannels);
            Push(samples, isSilent);
            capture.Get()->ReleaseBuffer(frames);
            PacketArrived?.Invoke((Clock.Now100ns() - (long)qpcPosition) / 10_000.0);
            capture.Get()->GetNextPacketSize(&packet);
        }
    }

    private void Push(ReadOnlySpan<float> declared, bool isSilent)
    {
        var source = declared;
        if (isMs2109)
        {
            var usable = declared[..Math.Min(declared.Length, quirkScratch.Length)];
            var frames = CardAudioQuirks.ReadMs2109(usable, quirkScratch);
            source = quirkScratch.AsSpan(0, frames * 2);
        }
        var peak = 0f;
        var mask = ring.Length - 1;
        foreach (var sample in source)
        {
            var value = isSilent ? 0 : sample;
            peak = Math.Max(peak, Math.Abs(value));
            ring[(int)(ringWrite++ & mask)] = value;
        }
        LevelDecibels = peak > 0 ? 20 * MathF.Log10(peak) : -120;
        // An overflow keeps the newest audio; the controller then drops to its target.
        if (ringWrite - ringRead > ring.Length) ringRead = ringWrite - ring.Length;
    }

    private void FillRender()
    {
        uint padding;
        renderClient.Get()->GetCurrentPadding(&padding);
        var available = renderBufferFrames - padding;
        if (available == 0) return;

        var now = Clock.Now100ns();
        var elapsedSeconds = (now - lastRenderTicks) / 10_000_000.0;
        lastRenderTicks = now;
        var bufferedFrames = (int)((ringWrite - ringRead) / captureChannels);

        byte* data;
        render.Get()->GetBuffer(available, &data);
        var output = new Span<float>(data, (int)available * renderChannels);
        if (refillUntilFrames is { } refillTarget)
        {
            if (bufferedFrames < refillTarget)
            {
                render.Get()->ReleaseBuffer(available, (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT);
                return;
            }
            refillUntilFrames = null;
        }
        switch (drift.Update(bufferedFrames, elapsedSeconds))
        {
            case DriftAction.Play play:
                Resample(output, play.InputFramesPerOutputFrame * captureRate / renderRate);
                render.Get()->ReleaseBuffer(available, 0);
                break;
            case DriftAction.DropTo drop:
                ringRead = ringWrite - ((long)drop.TargetFrames * captureChannels);
                readFraction = 0;
                render.Get()->ReleaseBuffer(available, (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT);
                break;
            case DriftAction.Refill refill:
                refillUntilFrames = refill.TargetFrames;
                render.Get()->ReleaseBuffer(available, (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT);
                break;
        }
    }

    /// <summary>Linear interpolation at <paramref name="step"/> card frames per output frame, into the output's channels.</summary>
    /// <remarks>ponytail: linear is enough within ±0.1 % of the card's rate; an MS2109 into a 44.1 kHz output would want a better filter.</remarks>
    private void Resample(Span<float> output, double step)
    {
        var mask = ring.Length - 1;
        var frames = output.Length / renderChannels;
        var gain = volume;
        for (var frame = 0; frame < frames; frame++)
        {
            var available = (ringWrite - ringRead) / captureChannels;
            if (available < 2)
            {
                output[(frame * renderChannels)..].Clear();
                return;
            }
            for (var channel = 0; channel < renderChannels; channel++)
            {
                float value = 0;
                if (channel < OutputChannelsUsed)
                {
                    var source = Math.Min(channel, captureChannels - 1);
                    var a = ring[(int)((ringRead + source) & mask)];
                    var b = ring[(int)((ringRead + captureChannels + source) & mask)];
                    value = (float)(a + ((b - a) * readFraction));
                }
                output[(frame * renderChannels) + channel] = value * gain;
            }
            readFraction += step;
            var whole = (long)readFraction;
            readFraction -= whole;
            ringRead += whole * captureChannels;
        }
    }

    private uint InitializeRender(WAVEFORMATEX* format, out uint periodFrames)
    {
        uint defaultPeriod, fundamental, minimum, maximum;
        renderClient.Get()->GetSharedModeEnginePeriod(format, &defaultPeriod, &fundamental, &minimum, &maximum);
        try
        {
            // The smallest period the driver allows (often 2.67 ms at 48 kHz on IAudioClient3-aware drivers).
            renderClient.Get()->InitializeSharedAudioStream(PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, minimum, format, null);
            periodFrames = minimum;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            renderClient.Get()->Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, 0, 0, format, null);
            periodFrames = defaultPeriod;
        }
        uint bufferFrames;
        renderClient.Get()->GetBufferSize(&bufferFrames);
        return bufferFrames;
    }

    private void InitializeCapture(WAVEFORMATEX* format, uint extraFlags)
    {
        const long tenMilliseconds = 100_000;
        captureClient.Get()->Initialize(
            AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK | extraFlags, tenMilliseconds * 2, 0, format, null);
    }

    /// <summary>No noise suppression or other microphone effects on the console's sound.</summary>
    private static void SetRawMode(IAudioClient3* client)
    {
        var properties = new AudioClientProperties
        {
            cbSize = (uint)sizeof(AudioClientProperties),
            eCategory = AUDIO_STREAM_CATEGORY.AudioCategory_Other,
            Options = AUDCLNT_STREAMOPTIONS.AUDCLNT_STREAMOPTIONS_RAW,
        };
        try
        {
            client->SetClientProperties(&properties);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Older drivers have no raw mode; their default processing still plays.
        }
    }

    private static WAVEFORMATEXTENSIBLE FloatFormat(int rate, int channels) => new()
    {
        Format = new WAVEFORMATEX
        {
            wFormatTag = (ushort)PInvoke.WAVE_FORMAT_EXTENSIBLE,
            nChannels = (ushort)channels,
            nSamplesPerSec = (uint)rate,
            wBitsPerSample = 32,
            nBlockAlign = (ushort)(4 * channels),
            nAvgBytesPerSec = (uint)(4 * channels * rate),
            cbSize = (ushort)(sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX)),
        },
        Samples = new WAVEFORMATEXTENSIBLE._Samples_e__Union { wValidBitsPerSample = 32 },
        dwChannelMask = 3,
        SubFormat = IeeeFloatSubFormat,
    };

    /// <summary><c>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT</c>: 32-bit float samples.</summary>
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private static bool IsFloat32(WAVEFORMATEX* format)
    {
        if (format->wBitsPerSample != 32) return false;
        if (format->wFormatTag == PInvoke.WAVE_FORMAT_IEEE_FLOAT) return true;
        return format->wFormatTag == PInvoke.WAVE_FORMAT_EXTENSIBLE && ((WAVEFORMATEXTENSIBLE*)format)->SubFormat == IeeeFloatSubFormat;
    }

    private static ComPtr<IMMDeviceEnumerator> CreateEnumerator()
    {
        MMDeviceEnumerator.CreateInstance<IMMDeviceEnumerator>(out var enumerator).ThrowOnFailure();
        return new ComPtr<IMMDeviceEnumerator>(enumerator);
    }

    private static ComPtr<IMMDevice> GetDevice(IMMDeviceEnumerator* enumerator, string id)
    {
        IMMDevice* device;
        fixed (char* text = id) enumerator->GetDevice(text, &device);
        return new ComPtr<IMMDevice>(device);
    }

    private static ComPtr<IMMDevice> DefaultOutput(IMMDeviceEnumerator* enumerator)
    {
        IMMDevice* device;
        enumerator->GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, &device);
        return new ComPtr<IMMDevice>(device);
    }

    private static ComPtr<IAudioClient3> Activate(IMMDevice* device)
    {
        void* client;
        var iid = IAudioClient3.IID_Guid;
        device->Activate(&iid, CLSCTX.CLSCTX_ALL, null, &client);
        return new ComPtr<IAudioClient3>((IAudioClient3*)client);
    }

    private static ComPtr<TService> Service<TService>(IAudioClient3* client) where TService : unmanaged, IComIID
    {
        void* service;
        var iid = TService.Guid;
        client->GetService(&iid, &service);
        return new ComPtr<TService>((TService*)service);
    }

    private static WAVEFORMATEX* MixFormat(IAudioClient3* client)
    {
        WAVEFORMATEX* format;
        client->GetMixFormat(&format);
        return format;
    }

    private static double StreamLatencyMilliseconds(IAudioClient3* client)
    {
        long latency;
        client->GetStreamLatency(&latency);
        return latency / 10_000.0;
    }

    private static string FriendlyName(IMMDevice* device) => StringProperty(device, PInvoke.PKEY_Device_FriendlyName);

    /// <summary>The bus the output's device sits on: <c>BTHENUM</c> or <c>BTHLEDEVICE</c> for Bluetooth.</summary>
    private static string EnumeratorName(IMMDevice* device) => StringProperty(device, PInvoke.PKEY_Device_EnumeratorName);

    private static string StringProperty(IMMDevice* device, PROPERTYKEY key)
    {
        IPropertyStore* rawStore;
        device->OpenPropertyStore(STGM.STGM_READ, &rawStore);
        using var store = new ComPtr<IPropertyStore>(rawStore);
        PROPVARIANT value;
        store.Get()->GetValue(&key, &value);
        try
        {
            return value.Anonymous.Anonymous.vt == VARENUM.VT_LPWSTR ? value.Anonymous.Anonymous.Anonymous.pwszVal.ToString() : "";
        }
        finally
        {
            PInvoke.PropVariantClear(&value);
        }
    }

    /// <summary>The USB vendor and product IDs in a device path such as <c>\\?\USB#VID_534D&amp;PID_2109&amp;MI_00#…</c>.</summary>
    internal static (ushort VendorId, ushort ProductId) UsbIds(string devicePath)
    {
        var match = UsbIdsPattern().Match(devicePath);
        if (!match.Success) return (0, 0);
        return (Convert.ToUInt16(match.Groups[1].Value, 16), Convert.ToUInt16(match.Groups[2].Value, 16));
    }

    [GeneratedRegex("VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UsbIdsPattern();

    private static int NextPowerOfTwo(int value) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)value);

    public void Dispose()
    {
        isStopping = true;
        thread?.Join(TimeSpan.FromSeconds(1));
        capture.Dispose();
        render.Dispose();
        captureClient.Dispose();
        renderClient.Dispose();
        PInvoke.CloseHandle(captureEvent);
        PInvoke.CloseHandle(renderEvent);
    }
}
