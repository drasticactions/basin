using System.Runtime.InteropServices;

namespace Basin.Eis;

/// <summary>
/// Tells whether libeis can load in this process.
/// </summary>
public static class EisLibrary
{
    private static readonly string[] Candidates = ["libeis.so.1", "libeis.so", "libeis"];

    private static bool? _available;
    private static string? _whyNot;

    /// <summary>
    /// Tries to load libeis once and keeps the result for later calls.
    /// </summary>
    /// <param name="whyNot">The cause when libeis is missing, or <see langword="null"/> when it loads.</param>
    /// <returns><see langword="true"/> when libeis loads.</returns>
    public static bool IsAvailable(out string? whyNot)
    {
        if (_available is { } known)
        {
            whyNot = _whyNot;
            return known;
        }

        foreach (var candidate in Candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                NativeLibrary.Free(handle);
                _available = true;
                _whyNot = null;
                whyNot = null;
                return true;
            }
        }

        _available = false;
        _whyNot = "libeis.so.1 is not installed";
        whyNot = _whyNot;
        return false;
    }
}
