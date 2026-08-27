using Basin.Avalonia;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static class HostCapture
{
    public static IDisposable? TryGrab(
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        CaptureHooks hooks)
    {
        Log.Warn($"this host cannot be grabbed, its own chords stay with it while the desktop is captured");
        return null;
    }
}
