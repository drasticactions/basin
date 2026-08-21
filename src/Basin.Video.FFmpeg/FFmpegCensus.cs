using Basin.Diagnostics;

namespace Basin.Video.FFmpeg;

internal static class FFmpegCensus
{
    public static void Track() => BasinCounters.Track();

    public static void Untrack() => BasinCounters.Untrack();
}
