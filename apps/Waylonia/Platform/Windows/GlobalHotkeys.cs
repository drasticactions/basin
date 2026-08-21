using System.Runtime.InteropServices;
using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Diagnostics;

namespace Waylonia;

internal static class GlobalHotkeys
{
    public static IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch) => WindowsGlobalHotkeys.TryStart(hotkeys, anchor, launch);
}
