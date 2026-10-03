namespace Plugview.Core.Audio;

/// <summary>A card whose audio is not what its USB descriptor says, and how to read it.</summary>
public enum CardAudioQuirk
{
    /// <summary>
    /// MacroSilicon MS2109 (USB 534D:2109), the most common cheap USB 2 card: it declares 96 kHz mono, but sends 48 kHz
    /// stereo, 16-bit, with left and right swapped. Linux fixes it in the kernel (snd-usb-audio, 5.9); Windows does not.
    /// </summary>
    Ms2109InterleavedStereo,
}

/// <summary>Finds and undoes card audio quirks (<c>spec/fixtures/card-quirks.json</c>).</summary>
public static class CardAudioQuirks
{
    private const ushort MacroSiliconVendorId = 0x534D;
    private const ushort Ms2109ProductId = 0x2109;

    /// <summary>The quirk that applies to a card's audio input, from its USB IDs and the format it declares; <c>null</c> for none.</summary>
    public static CardAudioQuirk? Detect(ushort vendorId, ushort productId, int declaredSampleRate, int declaredChannels) =>
        vendorId == MacroSiliconVendorId && productId == Ms2109ProductId && declaredSampleRate == 96_000 && declaredChannels == 1
            ? CardAudioQuirk.Ms2109InterleavedStereo
            : null;

    /// <summary>
    /// Reads MS2109 samples (declared 96 kHz mono) as 48 kHz stereo frames, left first, into <paramref name="stereo"/>.
    /// </summary>
    /// <remarks>Works on 16-bit samples and on the float samples Windows' shared mode delivers alike.</remarks>
    /// <returns>The number of stereo frames written: half the samples, rounded down; an odd last sample is left for the next call.</returns>
    public static int ReadMs2109<TSample>(ReadOnlySpan<TSample> declaredMono, Span<TSample> stereo) where TSample : unmanaged
    {
        var frames = declaredMono.Length / 2;
        if (stereo.Length < frames * 2) throw new ArgumentException("The output holds fewer samples than the input.", nameof(stereo));
        for (var frame = 0; frame < frames; frame++)
        {
            // The card sends right then left.
            stereo[2 * frame] = declaredMono[(2 * frame) + 1];
            stereo[(2 * frame) + 1] = declaredMono[2 * frame];
        }
        return frames;
    }
}
