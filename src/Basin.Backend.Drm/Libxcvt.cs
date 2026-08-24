using System.Runtime.InteropServices;

namespace Basin.Backend.Drm;

internal static unsafe class Libxcvt
{
    private const string Library = "libxcvt.so.0";

    [StructLayout(LayoutKind.Sequential)]
    internal struct ModeInfo
    {
        public uint HDisplay;
        public uint VDisplay;
        public float VRefresh;
        public float HSync;
        public ulong DotClock;
        public ushort HSyncStart;
        public ushort HSyncEnd;
        public ushort HTotal;
        public ushort VSyncStart;
        public ushort VSyncEnd;
        public ushort VTotal;
        public int ModeFlags;
    }

    internal const int FlagHSyncPositive = 1 << 0;
    internal const int FlagHSyncNegative = 1 << 1;
    internal const int FlagVSyncPositive = 1 << 2;
    internal const int FlagVSyncNegative = 1 << 3;

    private static readonly Lazy<bool> Available = new(Probe);

    internal static bool IsAvailable => Available.Value;

    internal static bool TryGenerate(int width, int height, int refreshMilliHz, bool reducedBlanking, out ModeInfo mode)
    {
        mode = default;
        if (!IsAvailable || width <= 0 || height <= 0 || refreshMilliHz <= 0)
        {
            return false;
        }

        var raw = libxcvt_gen_mode_info(width, height, refreshMilliHz / 1000.0f, reducedBlanking, false);
        if (raw is null)
        {
            return false;
        }

        mode = *raw;
        free(raw);
        return mode.HTotal > 0 && mode.VTotal > 0 && mode.DotClock > 0;
    }

    private static bool Probe()
    {
        try
        {
            var raw = libxcvt_gen_mode_info(640, 480, 60.0f, false, false);
            if (raw is null)
            {
                return false;
            }

            free(raw);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(Library)]
    private static extern ModeInfo* libxcvt_gen_mode_info(
        int hdisplay, int vdisplay, float vrefresh, [MarshalAs(UnmanagedType.I1)] bool reduced,
        [MarshalAs(UnmanagedType.I1)] bool interlaced);

    [DllImport("libc")]
    private static extern void free(void* pointer);
}
