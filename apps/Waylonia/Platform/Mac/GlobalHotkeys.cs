using System.Runtime.InteropServices;
using Basin.Avalonia;
using Basin.Diagnostics;

namespace Waylonia;

internal static class GlobalHotkeys
{
    public static IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch) => MacGlobalHotkeys.TryStart(hotkeys, launch);
}
