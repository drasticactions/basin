using System.Runtime.InteropServices;

namespace Basin.Screencast.PipeWire;

/// <summary>
/// Checks whether PipeWire can work in this process before a caller creates a <see cref="PipeWireScreencastPublisher"/>.
/// </summary>
public static class PipeWireLibraryProbe
{
    private static readonly string[] Candidates = ["libpipewire-0.3.so.0", "libpipewire-0.3.so", "libpipewire-0.3"];

    private static bool? _available;
    private static string? _whyNot;

    /// <summary>
    /// Tries to load <c>libpipewire-0.3</c> and caches the result for the life of the process.
    /// </summary>
    /// <param name="whyNot">The reason when the library cannot load, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the library loads, otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Checks for a PipeWire daemon without a connection. It looks for <c>PIPEWIRE_REMOTE</c> or a <c>pipewire-0</c> socket in <c>XDG_RUNTIME_DIR</c>.
    /// </summary>
    /// <returns><see langword="true"/> when a daemon socket is named or exists. The daemon can still refuse the connection.</returns>
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
