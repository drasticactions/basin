using Basin.Diagnostics;
using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

internal static partial class GraphiteNative
{
    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_graphite_recorder_snap")]
    public static extern nint RecorderSnap(nint recorder);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_graphite_recording_delete")]
    public static extern void RecordingDelete(nint recording);
}
