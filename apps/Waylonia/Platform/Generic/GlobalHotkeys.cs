using Basin.Avalonia;
using Basin.Diagnostics;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static class GlobalHotkeys
{
    public static IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch)
    {
        Log.Warn($"global hotkeys are not implemented on this host, the [hotkeys] table is ignored");
        return null;
    }
}
