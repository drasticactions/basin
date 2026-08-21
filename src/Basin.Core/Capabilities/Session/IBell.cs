using Wayland.Server;

namespace Basin.Capabilities;

public interface IBell
{
    void Ring(Surface? surface);
}
