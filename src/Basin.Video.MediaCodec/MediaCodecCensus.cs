using Basin.Diagnostics;

namespace Basin.Video.MediaCodec;

internal static class MediaCodecCensus
{
    public static void Track() => BasinCounters.Track();

    public static void Untrack() => BasinCounters.Untrack();
}
