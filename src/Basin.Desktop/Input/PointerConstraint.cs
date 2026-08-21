using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PointerConstraint
{
    private readonly PointerConstraintsManager _owner;
    private readonly ZwpLockedPointerV1Resource? _locked;
    private readonly ZwpConfinedPointerV1Resource? _confined;
    private bool _active;
    private bool _disposed;

    internal PointerConstraint(PointerConstraintsManager owner, Surface surface, ConstraintKind kind, bool persistent, WlClient client, uint version, uint id)
    {
        _owner = owner;
        Surface = surface;
        Kind = kind;
        IsPersistent = persistent;

        if (kind == ConstraintKind.Lock)
        {
            _locked = new ZwpLockedPointerV1Resource(client, version, id);
            _locked.Destroyed += (_, _) => Dispose();
            _locked.SetCursorPositionHint += (_, e) => OnHint(e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
            surface.Committed += OnCommitted;
        }
        else
        {
            _confined = new ZwpConfinedPointerV1Resource(client, version, id);
            _confined.Destroyed += (_, _) => Dispose();
        }

        surface.Destroyed += Dispose;
    }

    public Surface Surface { get; }

    public ConstraintKind Kind { get; }

    public bool IsPersistent { get; }

    public bool IsActive => _active;

    public (double X, double Y)? CursorPositionHint { get; private set; }

    public event Action? Deactivated;

    public void Activate()
    {
        if (_active || _disposed)
        {
            return;
        }

        _active = true;
        if (_locked is { IsDestroyed: false })
        {
            _locked.SendLocked();
        }

        if (_confined is { IsDestroyed: false })
        {
            _confined.SendConfined();
        }

        _owner.NotifyActivated(this);
    }

    public void Deactivate()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        if (_locked is { IsDestroyed: false })
        {
            _locked.SendUnlocked();
        }

        if (_confined is { IsDestroyed: false })
        {
            _confined.SendUnconfined();
        }

        Deactivated?.Invoke();
        if (!IsPersistent)
        {
            Dispose();
        }
    }

    internal void Teardown() => Dispose();

    private void OnHint(double x, double y)
    {
        if (_disposed)
        {
            return;
        }

        var carrier = _free.Count > 0 ? _free.Pop() : new PendingHint(this);
        carrier.Fill(x, y);
        Surface.Pending.SetExtension(carrier);
    }

    private void OnCommitted()
    {
        if (Surface.Current.TakeExtension<PendingHint>() is not { } carrier)
        {
            return;
        }

        using (carrier)
        {
            CursorPositionHint = (carrier.X, carrier.Y);
        }
    }

    private void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_active)
        {
            _active = false;
            Deactivated?.Invoke();
        }

        _owner.Remove(Surface);
        Surface.Destroyed -= Dispose;
        Surface.Committed -= OnCommitted;
        _free.Clear();
    }

    private readonly Stack<PendingHint> _free = new();

    private sealed class PendingHint(PointerConstraint owner) : IDisposable
    {
        public double X { get; private set; }

        public double Y { get; private set; }

        public void Fill(double x, double y) => (X, Y) = (x, y);

        public void Dispose() => owner._free.Push(this);
    }
}
