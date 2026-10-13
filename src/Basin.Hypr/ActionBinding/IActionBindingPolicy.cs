using Wayland.Server;

namespace Basin.Hypr;

public interface IActionBindingPolicy
{
    bool Allows(WlClient client);
}
