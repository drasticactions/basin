namespace Basin.Video.VideoToolbox;

internal static class Av1CodecConfiguration
{
    private const int ObuSequenceHeader = 1;

    internal static bool TryBuild(ReadOnlySpan<byte> temporalUnit, Span<byte> record, out int written)
    {
        written = 0;
        var offset = 0;
        while (offset < temporalUnit.Length)
        {
            var header = temporalUnit[offset];
            var type = (header >> 3) & 0xf;
            var hasExtension = (header & 0x4) != 0;
            var hasSize = (header & 0x2) != 0;
            var headerLength = hasExtension ? 2 : 1;
            if (!hasSize || offset + headerLength >= temporalUnit.Length)
            {
                return false;
            }

            var sizeStart = offset + headerLength;
            if (!TryReadLeb128(temporalUnit, ref sizeStart, out var size))
            {
                return false;
            }

            var payloadStart = sizeStart;
            var obuEnd = payloadStart + size;
            if (obuEnd > temporalUnit.Length)
            {
                return false;
            }

            if (type == ObuSequenceHeader)
            {
                var obu = temporalUnit[offset..obuEnd];
                var payload = temporalUnit[payloadStart..obuEnd];
                if (record.Length < 4 + obu.Length)
                {
                    return false;
                }

                if (!TryParseSequenceHeader(payload, record))
                {
                    return false;
                }

                obu.CopyTo(record[4..]);
                written = 4 + obu.Length;
                return true;
            }

            offset = obuEnd;
        }

        return false;
    }

    internal static bool ContainsSequenceHeader(ReadOnlySpan<byte> temporalUnit)
    {
        var offset = 0;
        while (offset < temporalUnit.Length)
        {
            var header = temporalUnit[offset];
            var type = (header >> 3) & 0xf;
            var headerLength = (header & 0x4) != 0 ? 2 : 1;
            if ((header & 0x2) == 0 || offset + headerLength >= temporalUnit.Length)
            {
                return false;
            }

            var sizeStart = offset + headerLength;
            if (!TryReadLeb128(temporalUnit, ref sizeStart, out var size))
            {
                return false;
            }

            if (type == ObuSequenceHeader)
            {
                return true;
            }

            offset = sizeStart + size;
        }

        return false;
    }

    private static bool TryReadLeb128(ReadOnlySpan<byte> bytes, ref int offset, out int value)
    {
        value = 0;
        for (var i = 0; i < 8; i++)
        {
            if (offset >= bytes.Length)
            {
                return false;
            }

            var b = bytes[offset++];
            value |= (b & 0x7f) << (7 * i);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseSequenceHeader(ReadOnlySpan<byte> payload, Span<byte> record)
    {
        var bits = new Bits(payload);
        var profile = bits.Read(3);
        bits.Read(1);
        var reduced = bits.Read(1) != 0;
        int levelIdx;
        var tier = 0;
        if (reduced)
        {
            levelIdx = bits.Read(5);
        }
        else
        {
            var timingInfoPresent = bits.Read(1) != 0;
            var decoderModelInfoPresent = false;
            var bufferDelayLength = 0;
            if (timingInfoPresent)
            {
                bits.Read(32);
                bits.Read(32);
                if (bits.Read(1) != 0)
                {
                    bits.ReadUvlc();
                }

                decoderModelInfoPresent = bits.Read(1) != 0;
                if (decoderModelInfoPresent)
                {
                    bufferDelayLength = bits.Read(5) + 1;
                    bits.Read(32);
                    bits.Read(5);
                    bits.Read(5);
                }
            }

            var initialDisplayDelayPresent = bits.Read(1) != 0;
            var operatingPoints = bits.Read(5) + 1;
            levelIdx = 0;
            for (var i = 0; i < operatingPoints; i++)
            {
                bits.Read(12);
                var level = bits.Read(5);
                var opTier = level > 7 ? bits.Read(1) : 0;
                if (i == 0)
                {
                    levelIdx = level;
                    tier = opTier;
                }

                if (decoderModelInfoPresent && bits.Read(1) != 0)
                {
                    bits.Read(bufferDelayLength);
                    bits.Read(bufferDelayLength);
                    bits.Read(1);
                }

                if (initialDisplayDelayPresent && bits.Read(1) != 0)
                {
                    bits.Read(4);
                }
            }
        }

        var widthBits = bits.Read(4) + 1;
        var heightBits = bits.Read(4) + 1;
        bits.Read(widthBits);
        bits.Read(heightBits);
        if (!reduced && bits.Read(1) != 0)
        {
            bits.Read(4);
            bits.Read(3);
        }

        bits.Read(1);
        bits.Read(1);
        bits.Read(1);
        var enableOrderHint = false;
        if (!reduced)
        {
            bits.Read(1);
            bits.Read(1);
            bits.Read(1);
            bits.Read(1);
            enableOrderHint = bits.Read(1) != 0;
            if (enableOrderHint)
            {
                bits.Read(1);
                bits.Read(1);
            }

            var forceScreenContentTools = bits.Read(1) != 0 ? 2 : bits.Read(1);
            if (forceScreenContentTools > 0)
            {
                if (bits.Read(1) == 0)
                {
                    bits.Read(1);
                }
            }

            if (enableOrderHint)
            {
                bits.Read(3);
            }
        }

        bits.Read(1);
        bits.Read(1);
        bits.Read(1);

        var highBitdepth = bits.Read(1);
        var twelveBit = 0;
        if (profile == 2 && highBitdepth != 0)
        {
            twelveBit = bits.Read(1);
        }

        var monochrome = profile == 1 ? 0 : bits.Read(1);
        var primaries = 2;
        var transfer = 2;
        var matrix = 2;
        if (bits.Read(1) != 0)
        {
            primaries = bits.Read(8);
            transfer = bits.Read(8);
            matrix = bits.Read(8);
        }

        int subsamplingX;
        int subsamplingY;
        var chromaPosition = 0;
        if (monochrome != 0)
        {
            bits.Read(1);
            subsamplingX = 1;
            subsamplingY = 1;
        }
        else if (primaries == 1 && transfer == 13 && matrix == 0)
        {
            subsamplingX = 0;
            subsamplingY = 0;
        }
        else
        {
            bits.Read(1);
            if (profile == 0)
            {
                subsamplingX = 1;
                subsamplingY = 1;
            }
            else if (profile == 1)
            {
                subsamplingX = 0;
                subsamplingY = 0;
            }
            else if (twelveBit != 0)
            {
                subsamplingX = bits.Read(1);
                subsamplingY = subsamplingX != 0 ? bits.Read(1) : 0;
            }
            else
            {
                subsamplingX = 1;
                subsamplingY = 0;
            }

            if (subsamplingX != 0 && subsamplingY != 0)
            {
                chromaPosition = bits.Read(2);
            }
        }

        record[0] = 0x81;
        record[1] = (byte)((profile << 5) | levelIdx);
        record[2] = (byte)((tier << 7) | (highBitdepth << 6) | (twelveBit << 5) | (monochrome << 4)
            | (subsamplingX << 3) | (subsamplingY << 2) | chromaPosition);
        record[3] = 0;
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

        internal void ReadUvlc()
        {
            var leadingZeros = 0;
            while (leadingZeros < 32 && Read(1) == 0)
            {
                leadingZeros++;
            }

            if (leadingZeros < 32)
            {
                Read(leadingZeros);
            }
        }
    }
}
