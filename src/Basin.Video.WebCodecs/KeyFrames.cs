using Basin.Capabilities;

namespace Basin.Video.WebCodecs;

internal static class KeyFrames
{
    internal static bool IsKey(VideoCodec codec, ReadOnlySpan<byte> packet) => codec switch
    {
        VideoCodec.H264 => H264HasIdr(packet),
        VideoCodec.Vp9 => Vp9IsKey(packet),
        _ => Av1HasSequenceHeader(packet),
    };

    private static bool H264HasIdr(ReadOnlySpan<byte> stream)
    {
        for (var i = 0; i + 3 < stream.Length; i++)
        {
            if (stream[i] != 0 || stream[i + 1] != 0)
            {
                continue;
            }

            int header;
            if (stream[i + 2] == 1)
            {
                header = i + 3;
            }
            else if (stream[i + 2] == 0 && i + 4 < stream.Length && stream[i + 3] == 1)
            {
                header = i + 4;
            }
            else
            {
                continue;
            }

            if (header < stream.Length && (stream[header] & 0x1f) == 5)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Vp9IsKey(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty)
        {
            return false;
        }

        var first = frame[0];
        if ((first >> 6) != 2)
        {
            return false;
        }

        var profile = ((first >> 5) & 1) | (((first >> 4) & 1) << 1);
        var shift = profile == 3 ? 2 : 3;
        var showExisting = (first >> shift) & 1;
        var frameType = (first >> (shift - 1)) & 1;
        return showExisting == 0 && frameType == 0;
    }

    private static bool Av1HasSequenceHeader(ReadOnlySpan<byte> temporalUnit)
    {
        var offset = 0;
        while (offset < temporalUnit.Length)
        {
            var header = temporalUnit[offset];
            var type = (header >> 3) & 0xf;
            if (type == 1)
            {
                return true;
            }

            var headerLength = (header & 0x4) != 0 ? 2 : 1;
            if ((header & 0x2) == 0)
            {
                return false;
            }

            var position = offset + headerLength;
            var size = 0;
            for (var i = 0; i < 8; i++)
            {
                if (position >= temporalUnit.Length)
                {
                    return false;
                }

                var b = temporalUnit[position++];
                size |= (b & 0x7f) << (7 * i);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            offset = position + size;
        }

        return false;
    }
}
