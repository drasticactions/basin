namespace Basin.Video.VideoToolbox;

internal static class H264AnnexB
{
    internal const int NalSps = 7;
    internal const int NalPps = 8;
    internal const int NalAccessUnitDelimiter = 9;
    internal const int NalIdr = 5;

    internal static bool TryNext(ReadOnlySpan<byte> stream, ref int offset, out int start, out int length)
    {
        start = -1;
        length = 0;
        var begin = FindStartCode(stream, offset, out var codeLength);
        if (begin < 0)
        {
            offset = stream.Length;
            return false;
        }

        start = begin + codeLength;
        var next = FindStartCode(stream, start, out _);
        var end = next < 0 ? stream.Length : next;
        while (end > start && stream[end - 1] == 0)
        {
            end--;
        }

        length = end - start;
        offset = next < 0 ? stream.Length : next;
        return length > 0;
    }

    internal static int TypeOf(ReadOnlySpan<byte> nal) => nal.IsEmpty ? -1 : nal[0] & 0x1f;

    private static int FindStartCode(ReadOnlySpan<byte> stream, int from, out int codeLength)
    {
        for (var i = from; i + 3 <= stream.Length; i++)
        {
            if (stream[i] != 0 || stream[i + 1] != 0)
            {
                continue;
            }

            if (stream[i + 2] == 1)
            {
                codeLength = 3;
                return i;
            }

            if (stream[i + 2] == 0 && i + 3 < stream.Length && stream[i + 3] == 1)
            {
                codeLength = 4;
                return i;
            }
        }

        codeLength = 0;
        return -1;
    }
}
