using System.Buffers.Binary;
using System.IO.Compression;

namespace Basin.Diagnostics;

public static class PngCodec
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("Expected tightly packed RGBA pixels.");
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(output, "IHDR", ihdr);

        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            rgba.Slice(y * width * 4, width * 4).CopyTo(raw.AsSpan(y * (width * 4 + 1) + 1));
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    public static (byte[] Rgba, int Width, int Height) Decode(ReadOnlySpan<byte> png)
    {
        if (!png[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        var width = 0;
        var height = 0;
        using var idat = new MemoryStream();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]);
            var type = System.Text.Encoding.ASCII.GetString(png.Slice(offset + 4, 4));
            var data = png.Slice(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    if (data[8] != 8 || data[9] != 6 || data[12] != 0)
                    {
                        throw new NotSupportedException("Only 8-bit non-interlaced RGBA is supported.");
                    }

                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        var rgba = new byte[width * height * 4];
        var lineBytes = width * 4;
        for (var y = 0; y < height; y++)
        {
            if (raw[y * (lineBytes + 1)] != 0)
            {
                throw new NotSupportedException("Only filter 0 is supported.");
            }

            raw.AsSpan(y * (lineBytes + 1) + 1, lineBytes).CopyTo(rgba.AsSpan(y * lineBytes));
        }

        return (rgba, width, height);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(data);

        var crc = Crc32(header[4..], data);
        Span<byte> footer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(footer, crc);
        output.Write(footer);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in a)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in b)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
