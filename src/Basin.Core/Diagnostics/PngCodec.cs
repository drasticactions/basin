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

    public static byte[] Encode16(ReadOnlySpan<ushort> rgb, int width, int height, PngColorChunks? color)
    {
        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException("Expected tightly packed 16-bit RGB samples.");
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 16;
        ihdr[9] = 2;
        WriteChunk(output, "IHDR", ihdr);

        if (color is { } chunks)
        {
            WriteChunk(output, "cICP", [chunks.Primaries, chunks.Transfer, 0, 1]);
            if (chunks.Mastering is { } hdr)
            {
                Span<byte> mdcv = stackalloc byte[24];
                BinaryPrimitives.WriteUInt16BigEndian(mdcv, hdr.PrimaryRed.X);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[2..], hdr.PrimaryRed.Y);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[4..], hdr.PrimaryGreen.X);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[6..], hdr.PrimaryGreen.Y);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[8..], hdr.PrimaryBlue.X);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[10..], hdr.PrimaryBlue.Y);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[12..], hdr.WhitePoint.X);
                BinaryPrimitives.WriteUInt16BigEndian(mdcv[14..], hdr.WhitePoint.Y);
                BinaryPrimitives.WriteUInt32BigEndian(mdcv[16..], hdr.MaxMasteringLuminance * 10000u);
                BinaryPrimitives.WriteUInt32BigEndian(mdcv[20..], hdr.MinMasteringLuminance);
                WriteChunk(output, "mDCV", mdcv);

                Span<byte> clli = stackalloc byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(clli, hdr.MaxContentLightLevel * 10000u);
                BinaryPrimitives.WriteUInt32BigEndian(clli[4..], hdr.MaxFrameAverageLightLevel * 10000u);
                WriteChunk(output, "cLLI", clli);
            }
        }

        var lineBytes = width * 6;
        var raw = new byte[(lineBytes + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var line = raw.AsSpan((y * (lineBytes + 1)) + 1, lineBytes);
            var samples = rgb.Slice(y * width * 3, width * 3);
            for (var i = 0; i < samples.Length; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(line[(i * 2)..], samples[i]);
            }
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

    public static bool TryReadChunk(ReadOnlySpan<byte> png, string type, out byte[] data)
    {
        data = [];
        if (png.Length < 8 || !png[..8].SequenceEqual(Signature))
        {
            return false;
        }

        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]);
            if (System.Text.Encoding.ASCII.GetString(png.Slice(offset + 4, 4)) == type)
            {
                data = png.Slice(offset + 8, length).ToArray();
                return true;
            }

            offset += 12 + length;
        }

        return false;
    }

    public static (ushort[] Rgb, int Width, int Height) Decode16(ReadOnlySpan<byte> png)
    {
        if (!TryReadChunk(png, "IHDR", out var ihdr) || ihdr[8] != 16 || ihdr[9] != 2 || ihdr[12] != 0)
        {
            throw new NotSupportedException("Only 16-bit non-interlaced RGB is supported.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        var height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4));
        using var idat = new MemoryStream();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]);
            if (System.Text.Encoding.ASCII.GetString(png.Slice(offset + 4, 4)) == "IDAT")
            {
                idat.Write(png.Slice(offset + 8, length));
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.GetBuffer();
        var lineBytes = width * 6;
        var previous = new byte[lineBytes];
        var line = new byte[lineBytes];
        var rgb = new ushort[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var start = y * (lineBytes + 1);
            Unfilter(raw[start], raw.AsSpan(start + 1, lineBytes), previous, line, 6);
            for (var i = 0; i < width * 3; i++)
            {
                rgb[(y * width * 3) + i] = BinaryPrimitives.ReadUInt16BigEndian(line.AsSpan(i * 2));
            }

            (previous, line) = (line, previous);
        }

        return (rgb, width, height);
    }

    public static (byte[] Rgba, int Width, int Height) Decode(ReadOnlySpan<byte> png)
    {
        if (!png[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        var width = 0;
        var height = 0;
        var channels = 0;
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
                    channels = data[9] switch
                    {
                        2 => 3,
                        6 => 4,
                        _ => 0,
                    };
                    if (data[8] != 8 || channels == 0 || data[12] != 0)
                    {
                        throw new NotSupportedException("Only 8-bit non-interlaced RGB or RGBA is supported.");
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

        var raw = inflated.GetBuffer();
        var lineBytes = width * channels;
        var previous = new byte[lineBytes];
        var line = new byte[lineBytes];
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var start = y * (lineBytes + 1);
            Unfilter(raw[start], raw.AsSpan(start + 1, lineBytes), previous, line, channels);
            var output = rgba.AsSpan(y * width * 4, width * 4);
            if (channels == 4)
            {
                line.CopyTo(output);
            }
            else
            {
                for (var x = 0; x < width; x++)
                {
                    output[x * 4] = line[x * 3];
                    output[(x * 4) + 1] = line[(x * 3) + 1];
                    output[(x * 4) + 2] = line[(x * 3) + 2];
                    output[(x * 4) + 3] = 0xFF;
                }
            }

            (previous, line) = (line, previous);
        }

        return (rgba, width, height);
    }

    private static void Unfilter(byte filter, ReadOnlySpan<byte> source, ReadOnlySpan<byte> above, Span<byte> line, int bpp)
    {
        for (var i = 0; i < source.Length; i++)
        {
            int left = i >= bpp ? line[i - bpp] : 0;
            int up = above[i];
            int upLeft = i >= bpp ? above[i - bpp] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) >> 1,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
            };
            line[i] = (byte)(source[i] + predictor);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
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
