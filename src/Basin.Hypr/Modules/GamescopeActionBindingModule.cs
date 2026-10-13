using Basin.Capabilities;

namespace Basin.Hypr;

public sealed class GamescopeActionBindingModule : IProtocolModule
{
    public string WireInterface => "gamescope_action_binding_manager";

    public int Version => ActionBindingManager.Version;

    public ActionBindingManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new ActionBindingManager(
            services.Display, services.Find<IGlobalShortcuts>(), services.Find<IActionBindingPolicy>());
        return Manager;
    }
}
