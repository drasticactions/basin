namespace Basin.Video.VideoToolbox;

internal static class Vp9CodecConfiguration
{
    internal static bool TryBuild(ReadOnlySpan<byte> frame, Span<byte> record, out int written)
    {
        written = 0;
        if (frame.Length < 10 || record.Length < 12)
        {
            return false;
        }

        var bits = new Bits(frame);
        if (bits.Read(2) != 2)
        {
            return false;
        }

        var profile = bits.Read(1) | (bits.Read(1) << 1);
        if (profile == 3)
        {
            bits.Read(1);
        }

        if (bits.Read(1) != 0)
        {
            return false;
        }

        var frameType = bits.Read(1);
        bits.Read(1);
        bits.Read(1);
        if (frameType != 0)
        {
            return false;
        }

        if (bits.Read(8) != 0x49 || bits.Read(8) != 0x83 || bits.Read(8) != 0x42)
        {
            return false;
        }

        var bitDepth = 8;
        if (profile >= 2)
        {
            bitDepth = bits.Read(1) != 0 ? 12 : 10;
        }

        var colorSpace = bits.Read(3);
        int fullRange;
        int subsamplingX;
        int subsamplingY;
        if (colorSpace != 7)
        {
            fullRange = bits.Read(1);
            if (profile is 1 or 3)
            {
                subsamplingX = bits.Read(1);
                subsamplingY = bits.Read(1);
                bits.Read(1);
            }
            else
            {
                subsamplingX = 1;
                subsamplingY = 1;
            }
        }
        else
        {
            fullRange = 1;
            subsamplingX = 0;
            subsamplingY = 0;
            if (profile is 1 or 3)
            {
                bits.Read(1);
            }
        }

        var chroma = (subsamplingX, subsamplingY) switch
        {
            (1, 1) => 1,
            (1, 0) => 2,
            _ => 3,
        };
        var (primaries, transfer, matrix) = colorSpace switch
        {
            1 or 3 => (6, 6, 6),
            2 => (1, 1, 1),
            4 => (7, 7, 7),
            5 => (9, 16, 9),
            7 => (1, 13, 0),
            _ => (2, 2, 2),
        };

        record[0] = 1;
        record[1] = 0;
        record[2] = 0;
        record[3] = 0;
        record[4] = (byte)profile;
        record[5] = 0;
        record[6] = (byte)((bitDepth << 4) | (chroma << 1) | fullRange);
        record[7] = (byte)primaries;
        record[8] = (byte)transfer;
        record[9] = (byte)matrix;
        record[10] = 0;
        record[11] = 0;
        written = 12;
        return true;
    }

    private ref struct Bits
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _position;

        internal Bits(ReadOnlySpan<byte> bytes) => _bytes = bytes;

        internal int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                var index = _position >> 3;
                var bit = index < _bytes.Length ? (_bytes[index] >> (7 - (_position & 7))) & 1 : 0;
                value = (value << 1) | bit;
                _position++;
            }

            return value;
        }
    }
}
