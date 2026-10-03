using System.Buffers.Binary;
using System.IO.Compression;

namespace Plugview.Windows.Capture;

/// <summary>Writes 8-bit BGRA pixels as an RGB PNG, with the standard library only (zlib and a CRC-32 table).</summary>
/// <remarks>ponytail: no WIC, no filters beyond "none"; a 1080p screenshot is a few MB, fine for a file on disk.</remarks>
internal static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(Stream output, uint width, uint height, ReadOnlySpan<byte> bgra)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (bgra.Length < width * height * 4) throw new ArgumentException("fewer pixels than width × height", nameof(bgra));
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB
        WriteChunk(output, "IHDR"u8, header);

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (width * 3)];
            for (var y = 0; y < height; y++)
            {
                // Filter type 0 (none), then the row as R, G, B.
                var source = bgra.Slice((int)(y * width * 4), (int)(width * 4));
                for (var x = 0; x < width; x++)
                {
                    row[1 + (x * 3)] = source[(x * 4) + 2];
                    row[2 + (x * 3)] = source[(x * 4) + 1];
                    row[3 + (x * 3)] = source[x * 4];
                }
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT"u8, pixels.GetBuffer().AsSpan(0, (int)pixels.Length));
        WriteChunk(output, "IEND"u8, []);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
