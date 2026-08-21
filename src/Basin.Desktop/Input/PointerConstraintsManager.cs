using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PointerConstraintsManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat? _seat;
    private readonly Dictionary<Surface, PointerConstraint> _bySurface = [];
    private readonly List<PointerConstraint> _sweep = [];

    public PointerConstraintsManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat? seat = null)
    {
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(ZwpPointerConstraintsV1.Interface, Version, OnBind);
        if (_seat is { } focused)
        {
            focused.Pointer.FocusChanged += FollowFocus;
        }
    }

    public event Action<PointerConstraint>? ConstraintCreated;

    public event Action<PointerConstraint>? ConstraintActivated;

    public PointerConstraint? ConstraintFor(Surface surface) =>
        _bySurface.TryGetValue(surface, out var constraint) ? constraint : null;

    public void Dispose()
    {
        if (_seat is { } focused)
        {
            focused.Pointer.FocusChanged -= FollowFocus;
        }

        var live = new PointerConstraint[_bySurface.Count];
        _bySurface.Values.CopyTo(live, 0);
        foreach (var constraint in live)
        {
            constraint.Teardown();
        }

        _bySurface.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpPointerConstraintsV1Resource(client, version, id);
        manager.LockPointer += (_, e) => Create(client, manager.Version, e.Id, e.Surface, ConstraintKind.Lock, (uint)e.Lifetime, lockResource: null);
        manager.ConfinePointer += (_, e) => Create(client, manager.Version, e.Id, e.Surface, ConstraintKind.Confine, (uint)e.Lifetime, lockResource: null);
    }

    private void Create(WlClient client, uint version, uint id, WlSurfaceResource? surfaceResource, ConstraintKind kind, uint lifetime, object? lockResource)
    {
        var surface = _compositor.ResolveSurface(surfaceResource);
        if (surface is null)
        {
            return;
        }

        if (_bySurface.ContainsKey(surface))
        {
            if (kind == ConstraintKind.Lock)
            {
                new ZwpLockedPointerV1Resource(client, version, id).PostError(
                    (uint)ZwpPointerConstraintsV1.Error.AlreadyConstrained, "surface already constrained");
            }
            else
            {
                new ZwpConfinedPointerV1Resource(client, version, id).PostError(
                    (uint)ZwpPointerConstraintsV1.Error.AlreadyConstrained, "surface already constrained");
            }

            return;
        }

        var constraint = new PointerConstraint(this, surface, kind, lifetime == 2, client, version, id);
        _bySurface[surface] = constraint;
        ConstraintCreated?.Invoke(constraint);
        if (_seat?.Pointer.Focus == surface)
        {
            constraint.Activate();
        }
    }

    private void FollowFocus(Surface? surface)
    {
        _sweep.Clear();
        _sweep.AddRange(_bySurface.Values);
        foreach (var constraint in _sweep)
        {
            if (constraint.Surface != surface && constraint.IsActive)
            {
                constraint.Deactivate();
            }
        }

        _sweep.Clear();
        if (surface is not null && _bySurface.TryGetValue(surface, out var focused))
        {
            focused.Activate();
        }
    }

    internal void NotifyActivated(PointerConstraint constraint) => ConstraintActivated?.Invoke(constraint);

    internal void Remove(Surface surface) => _bySurface.Remove(surface);
}
