using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Render.Skia;

public static class SkiaCensus
{
    public static T Track<T>(T obj, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : SKObject
    {
        BasinCounters.Track(1, file, line);
        return obj;
    }

    public static void Release(SKObject? obj, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (obj is null)
        {
            return;
        }

        BasinCounters.Untrack(1, file, line);
        obj.Dispose();
    }
}
