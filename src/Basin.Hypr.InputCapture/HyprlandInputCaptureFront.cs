using Basin.Eis;
using Basin.Hypr.InputCapture.Protocol;
using Wayland;

namespace Basin.Hypr.InputCapture;

internal sealed class HyprlandInputCaptureFront : IInputCaptureFront, IDisposable
{
    private readonly HyprlandInputCaptureManager _owner;
    private readonly HyprlandInputCaptureV1Resource _resource;
    private bool _disposed;

    public HyprlandInputCaptureFront(HyprlandInputCaptureManager owner, HyprlandInputCaptureV1Resource resource, string handle)
    {
        _owner = owner;
        _resource = resource;
        Session = owner.Engine.CreateSession(this, handle);

        resource.Enable += (_, _) => Session.Enable();
        resource.Disable += (_, _) => Session.Disable();
        resource.ClearBarriers += (_, _) => Session.ClearBarriers();
        resource.AddBarrier += (_, e) => OnAddBarrier(e.Id, e.X1, e.Y1, e.X2, e.Y2);
        resource.Release += (_, e) => OnRelease(e.ActivationId, e.X.ToDouble(), e.Y.ToDouble());
        resource.Destroyed += (_, _) => Dispose();
    }

    public InputCaptureSession Session { get; }

    public void Activated(uint activationId, double x, double y, uint barrierId)
    {
        if (!_resource.IsDestroyed)
        {
            _resource.SendActivated(activationId, WlFixed.FromDouble(x), WlFixed.FromDouble(y), barrierId);
        }
    }

    public void Deactivated(uint activationId)
    {
        if (!_resource.IsDestroyed)
        {
            _resource.SendDeactivated(activationId);
        }
    }

    public void Disabled()
    {
        if (!_resource.IsDestroyed)
        {
            _resource.SendDisabled();
        }
    }

    public void ZonesChanged()
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.Dispose();
        _owner.Forget(this);
    }

    private void OnAddBarrier(uint id, uint x1, uint y1, uint x2, uint y2)
    {
        var barrier = InputCaptureBarriers.FromWire(id, x1, y1, x2, y2);
        if (Session.HasBarrier(id))
        {
            _resource.PostError((uint)HyprlandInputCaptureV1.Error.InvalidBarrierId, $"barrier {id} already exists");
            return;
        }

        if (!Session.TryAddBarrier(in barrier))
        {
            _resource.PostError((uint)HyprlandInputCaptureV1.Error.InvalidBarrier, $"barrier {id} is not an output edge");
        }
    }

    private void OnRelease(uint activationId, double x, double y)
    {
        if (!Session.Release(activationId, x, y))
        {
            _resource.PostError(
                (uint)HyprlandInputCaptureV1.Error.InvalidActivationId,
                $"activation id {activationId} is not the current {Session.ActivationId}");
        }
    }
}
