using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal sealed class RiverPointerBinding
{
    private readonly RiverWindowManager _manager;
    private RiverPointerBindingV1Resource? _resource;

    internal RiverPointerBinding(
        RiverWindowManager manager,
        RiverPointerBindingV1Resource resource,
        uint button,
        RiverSeatV1.Modifiers modifiers)
    {
        _manager = manager;
        _resource = resource;
        Button = button;
        Modifiers = modifiers;

        resource.Enable += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                IsEnabled = true;
            }
        };
        resource.Disable += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                IsEnabled = false;
            }
        };
        resource.DestroyRequest += (_, _) => _resource = null;
    }

    internal uint Button { get; }

    internal RiverSeatV1.Modifiers Modifiers { get; }

    internal bool IsEnabled { get; private set; }

    internal void SendPressed()
    {
        if (_resource is { IsDestroyed: false } resource)
        {
            resource.SendPressed();
        }
    }

    internal void SendReleased()
    {
        if (_resource is { IsDestroyed: false } resource)
        {
            resource.SendReleased();
        }
    }
}
