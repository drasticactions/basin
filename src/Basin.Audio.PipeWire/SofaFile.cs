using System.Runtime.InteropServices;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Probes a SOFA HRTF file through libmysofa, which the sofa plugin of PipeWire also uses.
/// </summary>
public static unsafe class SofaFile
{
    private const string Library = "libmysofa.so.1";

    /// <summary>
    /// Opens a SOFA file at 48 kHz and reads its measured radius range. Copy the range to <see cref="VoiceGraph.HrtfRadiusMin"/> and <see cref="VoiceGraph.HrtfRadiusMax"/>.
    /// </summary>
    /// <param name="path">The path of the SOFA file.</param>
    /// <param name="radiusMin">The smallest measured radius in meters, or 1 when the probe fails or the file has no lookup.</param>
    /// <param name="radiusMax">The largest measured radius in meters, or 1 when the probe fails or the file has no lookup.</param>
    /// <param name="reason">Why the probe failed, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when libmysofa loads and reads the file. <see langword="false"/> when the file is absent, libmysofa is not installed, or it cannot read the file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static bool TryProbe(string path, out float radiusMin, out float radiusMax, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(path);
        radiusMin = radiusMax = 1;
        if (!File.Exists(path))
        {
            reason = $"{path} does not exist";
            return false;
        }

        if (!NativeLibrary.TryLoad(Library, out var library))
        {
            reason = $"{Library} is not installed";
            return false;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(library, "mysofa_open", out var open) ||
                !NativeLibrary.TryGetExport(library, "mysofa_close", out var close))
            {
                reason = $"{Library} has no mysofa_open";
                return false;
            }

            var openFunction = (delegate* unmanaged<byte*, float, int*, int*, Easy*>)open;
            var closeFunction = (delegate* unmanaged<Easy*, void>)close;
            var name = System.Text.Encoding.UTF8.GetBytes(path + "\0");
            int length, error;
            Easy* easy;
            fixed (byte* file = name)
            {
                easy = openFunction(file, 48000, &length, &error);
            }

            if (easy is null || error != 0)
            {
                reason = $"libmysofa cannot read {path} (error {error})";
                if (easy is not null)
                {
                    closeFunction(easy);
                }

                return false;
            }

            if (easy->Lookup is not null)
            {
                radiusMin = easy->Lookup->RadiusMin;
                radiusMax = easy->Lookup->RadiusMax;
            }

            closeFunction(easy);
            reason = null;
            return true;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Easy
    {
        public void* Hrtf;
        public Lookup* Lookup;
        public void* Neighborhood;
        public float* Fir;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Lookup
    {
        public void* Kdtree;
        public float RadiusMin;
        public float RadiusMax;
        public float ThetaMin;
        public float ThetaMax;
        public float PhiMin;
        public float PhiMax;
    }
}
