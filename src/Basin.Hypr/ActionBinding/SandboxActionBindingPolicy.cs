using Basin.Desktop;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class SandboxActionBindingPolicy : IActionBindingPolicy
{
    public bool Allows(WlClient client) => SecurityContextManager.ContextOf(client) is null;
}
