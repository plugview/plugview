using Plugview.Windows.Audio;
using Plugview.Windows.Video;

namespace Plugview.Windows.Tests;

public sealed class GeometryAndParsingTests
{
    [Theory]
    [InlineData(1920u, 1080u, 1280u, 720u, 0, 0, 1280, 720)]
    [InlineData(1920u, 1080u, 1280u, 1000u, 0, 140, 1280, 860)]
    [InlineData(1280u, 800u, 1920u, 1080u, 96, 0, 1824, 1080)]
    [InlineData(640u, 480u, 1920u, 1080u, 240, 0, 1680, 1080)]
    public void LetterboxesWithoutStretching(uint videoWidth, uint videoHeight, uint windowWidth, uint windowHeight, int left, int top, int right, int bottom)
    {
        var picture = VideoPresenter.Letterbox(videoWidth, videoHeight, windowWidth, windowHeight);
        Assert.Equal((left, top, right, bottom), (picture.left, picture.top, picture.right, picture.bottom));
    }

    [Theory]
    [InlineData(@"\\?\USB#VID_534D&PID_2109&MI_00#7&2f4b3c1&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\GLOBAL", 0x534D, 0x2109)]
    [InlineData(@"\\?\usb#vid_345f&pid_2130&mi_00#8&1234#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global", 0x345F, 0x2130)]
    [InlineData(@"\\?\SWD#VCAMDEVAPI#{abc}", 0, 0)]
    public void ReadsUsbIdsFromDevicePaths(string path, int vendorId, int productId)
    {
        Assert.Equal(((ushort)vendorId, (ushort)productId), AudioPassthrough.UsbIds(path));
    }
}
