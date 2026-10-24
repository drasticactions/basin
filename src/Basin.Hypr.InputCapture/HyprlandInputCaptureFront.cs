using Basin.Eis;
using Basin.Hypr.InputCapture.Protocol;
using Wayland;

namespace Basin.Hypr.InputCapture;

internal sealed class HyprlandInputCaptureFront : IInputCaptureFront, IDisposable, IHyprlandInputCaptureV1Handler
{
    private readonly HyprlandInputCaptureManager _owner;
    private readonly HyprlandInputCaptureV1Resource _resource;
    private bool _disposed;
    private bool _wired;

    public HyprlandInputCaptureFront(HyprlandInputCaptureManager owner, Wayland.Server.WlClient client, uint version, uint id, string handle)
    {
        _owner = owner;
        _resource = new HyprlandInputCaptureV1Resource(client, version, id, this);
        Session = owner.Engine.CreateSession(this, handle);

        _wired = true;
        _resource.Destroyed += (_, _) => Dispose();
    }

    public HyprlandInputCaptureV1Resource Resource => _resource;

    void IHyprlandInputCaptureV1Handler.Enable(HyprlandInputCaptureV1Resource resource)
    {
        if (_wired)
        {
            Session.Enable();
        }
    }

    void IHyprlandInputCaptureV1Handler.Disable(HyprlandInputCaptureV1Resource resource)
    {
        if (_wired)
        {
            Session.Disable();
        }
    }

    void IHyprlandInputCaptureV1Handler.ClearBarriers(HyprlandInputCaptureV1Resource resource)
    {
        if (_wired)
        {
            Session.ClearBarriers();
        }
    }

    void IHyprlandInputCaptureV1Handler.AddBarrier(HyprlandInputCaptureV1Resource resource, in HyprlandInputCaptureV1Resource.AddBarrierEventArgs args)
    {
        if (_wired)
        {
            OnAddBarrier(args.Id, args.X1, args.Y1, args.X2, args.Y2);
        }
    }

    void IHyprlandInputCaptureV1Handler.Release(HyprlandInputCaptureV1Resource resource, in HyprlandInputCaptureV1Resource.ReleaseEventArgs args)
    {
        if (_wired)
        {
            OnRelease(args.ActivationId, args.X.ToDouble(), args.Y.ToDouble());
        }
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
            _resource.PostError((uint)HyprlandInputCaptureV1.Error.InvalidBarrierId, $"Barrier {id} already exists.");
            return;
        }

        if (!Session.TryAddBarrier(in barrier))
        {
            _resource.PostError((uint)HyprlandInputCaptureV1.Error.InvalidBarrier, $"Barrier {id} is not on an output edge.");
        }
    }

    private void OnRelease(uint activationId, double x, double y)
    {
        if (!Session.Release(activationId, x, y))
        {
            _resource.PostError(
                (uint)HyprlandInputCaptureV1.Error.InvalidActivationId,
                $"Activation id {activationId} is not the current activation id {Session.ActivationId}.");
        }
    }
}
