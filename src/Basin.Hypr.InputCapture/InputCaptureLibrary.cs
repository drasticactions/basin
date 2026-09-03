using System.Runtime.InteropServices;

namespace Basin.Hypr.InputCapture;

public static class InputCaptureLibrary
{
    private static readonly string[] Candidates = ["libeis.so.1", "libeis.so", "libeis"];

    private static bool? _available;
    private static string? _whyNot;

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
