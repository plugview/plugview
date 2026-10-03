using System.Buffers.Binary;
using System.IO.Compression;
using Plugview.Core.Launcher;
using Plugview.Core.Selection;
using Plugview.Windows.App;
using Plugview.Windows.Capture;
using Plugview.Windows.Graphics;
using Plugview.Windows.Ui;
using Plugview.Windows.Video;

namespace Plugview.Windows.Tests;

public sealed class AppPartsTests
{
    [Fact]
    public void ScreenshotsAreValidRgbPngs()
    {
        // 2×1: a red and a blue pixel, as BGRA.
        byte[] bgra = [0, 0, 255, 255, 255, 0, 0, 255];
        using var file = new MemoryStream();
        PngWriter.Write(file, 2, 1, bgra);
        var png = file.ToArray();

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(chunk => chunk.Type));
        var header = chunks[0].Data;
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(header));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        Assert.Equal((byte)2, header[9]);

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress)) zlib.CopyTo(inflated);
        // Filter byte 0, then R, G, B per pixel.
        Assert.Equal([0, 255, 0, 0, 0, 0, 255], inflated.ToArray());
    }

    [Fact]
    public void TrayIconsUseTheNearestImageScalingDown()
    {
        var ico = Ico(16, 24, 32);
        Assert.Equal((byte)24, AppController.IconImage(ico, 20)!.Value.Span[0]);
        Assert.Equal((byte)16, AppController.IconImage(ico, 16)!.Value.Span[0]);
        Assert.Equal((byte)32, AppController.IconImage(ico, 40)!.Value.Span[0]);
        Assert.Null(AppController.IconImage([0, 0], 16));
    }

    [Fact]
    public void BlackFramesHaveLowLuma()
    {
        Assert.Equal(0, new FrameGrab(1, 1, [0, 0, 0, 255]).MaxLuma());
        Assert.Equal(255, new FrameGrab(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]).MaxLuma());
        // Pure green carries most of the luma (BT.709).
        Assert.Equal(182, new FrameGrab(1, 1, [0, 255, 0, 255]).MaxLuma());
    }

    [Fact]
    public void ClicksGoToTheTopmostControlAndTabCyclesThroughControls()
    {
        var frame = new UiFrame();
        frame.Add(new UiElement("title", new Box(0, 0, 100, 20), UiRole.Text, "SIGNAL FOUND"));
        frame.Add(new UiElement("play", new Box(0, 30, 100, 40), UiRole.Button, "Play", () => { }));
        frame.Add(new UiElement("help", new Box(0, 80, 100, 40), UiRole.Button, "Help me connect", () => { }));
        frame.Add(new UiElement("toast", new Box(0, 30, 100, 10), UiRole.Button, "Show", () => { }));

        Assert.Equal("toast", frame.HitTest(10, 35)?.Id);
        Assert.Equal("play", frame.HitTest(10, 50)?.Id);
        Assert.Null(frame.HitTest(10, 10));
        Assert.Equal("play", frame.NextFocus(null, isBackward: false)?.Id);
        Assert.Equal("help", frame.NextFocus("play", isBackward: false)?.Id);
        Assert.Equal("play", frame.NextFocus("toast", isBackward: false)?.Id);
        Assert.Equal("toast", frame.NextFocus("play", isBackward: true)?.Id);
    }

    [Fact]
    public void EveryLauncherStateHasItsScreen()
    {
        var device = new DeviceInfo("id", "UGREEN 25773", "model", DeviceTransport.Usb);
        LauncherState[] states =
        [
            new LauncherState.PermissionNeeded(PermissionKind.Camera),
            new LauncherState.PermissionDenied(PermissionKind.Microphone),
            new LauncherState.WaitingForSignal(),
            new LauncherState.Ready([device]),
            new LauncherState.Playing(device),
            new LauncherState.Failed(new CaptureFailure("UGREEN 25773", "in use")),
        ];
        var ids = states.Select(state => LauncherView.ScreenId(new LauncherModel(state, _ => "", null, false, null, () => { }, _ => { }))).ToList();
        Assert.Equal(["permission-prompt", "denied-Microphone", "waiting", "ready", "playing", "failed"], ids);
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            chunks.Add((type, png[(offset + 8)..(offset + 8 + length)]));
            offset += 12 + length;
        }
        return chunks;
    }

    /// <summary>An ICO with one image per size; each image's first byte is its size, so a test can tell them apart.</summary>
    private static byte[] Ico(params int[] sizes)
    {
        var header = 6 + (16 * sizes.Length);
        var ico = new byte[header + sizes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), (ushort)sizes.Length);
        for (var index = 0; index < sizes.Length; index++)
        {
            var entry = ico.AsSpan(6 + (16 * index), 16);
            entry[0] = (byte)sizes[index];
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)(header + index));
            ico[header + index] = (byte)sizes[index];
        }
        return ico;
    }
}
