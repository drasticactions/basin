using System.Runtime.InteropServices;

namespace Basin.Screencast.PipeWire;

public static class PipeWireLibraryProbe
{
    private static readonly string[] Candidates = ["libpipewire-0.3.so.0", "libpipewire-0.3.so", "libpipewire-0.3"];

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
        _whyNot = "libpipewire-0.3.so.0 is not installed";
        whyNot = _whyNot;
        return false;
    }

    public static bool IsDaemonReachable()
    {
        if (Environment.GetEnvironmentVariable("PIPEWIRE_REMOTE") is { Length: > 0 })
        {
            return true;
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return runtime is { Length: > 0 } && File.Exists(Path.Combine(runtime, "pipewire-0"));
    }
}
