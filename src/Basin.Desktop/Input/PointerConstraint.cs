using Basin.Desktop.Protocol;
using Pixman;
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
    private readonly PixmanRegion32 _region = new();
    private readonly PixmanRegion32 _pendingRegion = new();
    private readonly PixmanRegion32 _effective = new();
    private bool _regionInfinite = true;
    private bool _pendingRegionInfinite = true;
    private bool _regionPending;
    private bool _effectiveStale = true;

    internal PointerConstraint(PointerConstraintsManager owner, Surface surface, ConstraintKind kind, bool persistent, Region? region, WlClient client, uint version, uint id)
    {
        _owner = owner;
        Surface = surface;
        Kind = kind;
        IsPersistent = persistent;
        if (region is not null)
        {
            _region.Copy(region.Pixman);
            ServerScaling.ToLogical(_region, surface.ServerScale, outward: true, _effective);
            _regionInfinite = false;
        }

        if (kind == ConstraintKind.Lock)
        {
            _locked = new ZwpLockedPointerV1Resource(client, version, id);
            _locked.Destroyed += (_, _) => Dispose();
            _locked.SetCursorPositionHint += (_, e) => OnHint(e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
            _locked.SetRegion += (_, e) => OnSetRegion(_owner.ResolveRegion(e.Region));
        }
        else
        {
            _confined = new ZwpConfinedPointerV1Resource(client, version, id);
            _confined.Destroyed += (_, _) => Dispose();
            _confined.SetRegion += (_, e) => OnSetRegion(_owner.ResolveRegion(e.Region));
        }

        surface.Committed += OnCommitted;
        surface.Destroyed += Dispose;
    }

    public Surface Surface { get; }

    public ConstraintKind Kind { get; }

    public bool IsPersistent { get; }

    public bool IsActive => _active;

    public (double X, double Y)? CursorPositionHint { get; private set; }

    public event Action? Deactivated;

    public bool Contains(double surfaceX, double surfaceY)
    {
        var region = Effective();
        return surfaceX >= 0 && surfaceY >= 0 && region.Contains((int)Math.Floor(surfaceX), (int)Math.Floor(surfaceY));
    }

    public bool Clamp(ref double surfaceX, ref double surfaceY)
    {
        if (Contains(surfaceX, surfaceY))
        {
            return false;
        }

        var region = Effective();
        if (region.IsEmpty)
        {
            return false;
        }

        var bestX = surfaceX;
        var bestY = surfaceY;
        var best = double.MaxValue;
        foreach (var box in region.Rectangles())
        {
            var x = Math.Clamp(surfaceX, box.X1, box.X2 - Subpixel);
            var y = Math.Clamp(surfaceY, box.Y1, box.Y2 - Subpixel);
            var distance = ((x - surfaceX) * (x - surfaceX)) + ((y - surfaceY) * (y - surfaceY));
            if (distance < best)
            {
                (best, bestX, bestY) = (distance, x, y);
            }
        }

        surfaceX = bestX;
        surfaceY = bestY;
        return true;
    }

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

    private const double Subpixel = 1.0 / 256;

    private void OnSetRegion(Region? region)
    {
        if (_disposed)
        {
            return;
        }

        _pendingRegion.Clear();
        _pendingRegionInfinite = region is null;
        if (region is not null)
        {
            _pendingRegion.Copy(region.Pixman);
        }

        _regionPending = true;
    }

    private PixmanRegion32 Effective()
    {
        if (!_effectiveStale)
        {
            return _effective;
        }

        _effectiveStale = false;
        var current = Surface.Current;
        _effective.Reset(new PixmanBox32(0, 0, Math.Max(0, current.Width), Math.Max(0, current.Height)));
        if (!current.InputIsInfinite)
        {
            _effective.IntersectWith(current.Input);
        }

        if (!_regionInfinite)
        {
            _effective.IntersectWith(_region);
        }

        return _effective;
    }

    private void OnCommitted()
    {
        _effectiveStale = true;
        if (_regionPending)
        {
            _regionPending = false;
            _regionInfinite = _pendingRegionInfinite;
            _region.Clear();
            if (!_regionInfinite)
            {
                _region.Copy(_pendingRegion);
                ServerScaling.ToLogical(_region, Surface.ServerScale, outward: true, _effective);
            }
        }

        if (Surface.Current.TakeExtension<PendingHint>() is not { } carrier)
        {
            return;
        }

        using (carrier)
        {
            var scale = Surface.ServerScale;
            CursorPositionHint = (carrier.X / scale, carrier.Y / scale);
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

        _owner.Remove(this);
        Surface.Destroyed -= Dispose;
        Surface.Committed -= OnCommitted;
        _free.Clear();
        _region.Dispose();
        _pendingRegion.Dispose();
        _effective.Dispose();
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
