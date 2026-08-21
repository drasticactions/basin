using Wayland.Server;

namespace Basin.Capabilities.Defaults;

public sealed class SilentBell : IBell
{
    public static SilentBell Instance { get; } = new();

    public int Rings { get; private set; }

    public void Ring(Surface? surface) => Rings++;
}
