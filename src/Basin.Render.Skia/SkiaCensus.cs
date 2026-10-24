using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// Counts live Skia handles in the basin leak counters. Every <see cref="SKObject"/> that basin
/// creates goes through <see cref="Track"/> and <see cref="Release"/>.
/// </summary>
public static class SkiaCensus
{
    /// <summary>
    /// Counts one live Skia handle against the caller's file and line.
    /// </summary>
    /// <typeparam name="T">The Skia handle type.</typeparam>
    /// <param name="obj">The new handle. It must not be null.</param>
    /// <param name="file">The caller's source file, filled in by the compiler.</param>
    /// <param name="line">The caller's source line, filled in by the compiler.</param>
    /// <returns>The same <paramref name="obj"/>, so the call can wrap a constructor.</returns>
    public static T Track<T>(T obj, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : SKObject
    {
        BasinCounters.Track(1, file, line);
        return obj;
    }

    /// <summary>
    /// Uncounts and disposes a Skia handle that <see cref="Track"/> counted. A null handle does
    /// nothing.
    /// </summary>
    /// <param name="obj">The handle to dispose, or <see langword="null"/>.</param>
    /// <param name="file">The caller's source file, filled in by the compiler.</param>
    /// <param name="line">The caller's source line, filled in by the compiler.</param>
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
