using Basin.Capabilities;

namespace Basin.Ipc;

internal readonly record struct IpcTarget(string Kind, string Selector, ToplevelInfo? Window, Surface? Surface, Func<Box?> Follow)
{
    public ulong WindowId => Window?.Id ?? 0;

    public bool HasFrame => Kind is "window" or "x11";
}
