using Basin.Diagnostics;

namespace Basin.Video.VideoToolbox;

internal static class VideoToolboxCensus
{
    public static void Track() => BasinCounters.Track();

    public static void Untrack() => BasinCounters.Untrack();
}
