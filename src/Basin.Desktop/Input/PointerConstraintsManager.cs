using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_pointer_constraints_v1</c> global and keeps at most one constraint per surface. A second request for a constrained surface is a protocol error.
/// </summary>
public sealed class PointerConstraintsManager : IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, PointerConstraint> _bySurface = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerConstraintsManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="compositor">The compositor that resolves surfaces and regions.</param>
    /// <param name="seat">The seat to drive constraints on. When it is <see langword="null"/>, no <see cref="Driver"/> exists and the consumer must activate constraints.</param>
    public PointerConstraintsManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat? seat = null)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(ZwpPointerConstraintsV1.Interface, Version, OnBind);
        if (seat is not null)
        {
            Driver = new PointerConstraintDriver(this, seat);
        }
    }

    /// <summary>
    /// Gets the driver that activates constraints on the seat. It is <see langword="null"/> when the manager has no seat.
    /// </summary>
    public PointerConstraintDriver? Driver { get; }

    /// <summary>
    /// Occurs when a client creates a constraint, before the driver evaluates it.
    /// </summary>
    public event Action<PointerConstraint>? ConstraintCreated;

    /// <summary>
    /// Occurs when a constraint becomes active.
    /// </summary>
    public event Action<PointerConstraint>? ConstraintActivated;

    /// <summary>
    /// Occurs when a constraint is destroyed by the client, by its surface, or by a oneshot deactivation.
    /// </summary>
    public event Action<PointerConstraint>? ConstraintRemoved;

    /// <summary>
    /// Finds the constraint on a surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The constraint, or <see langword="null"/> when the surface has none.</returns>
    public PointerConstraint? ConstraintFor(Surface surface) =>
        _bySurface.TryGetValue(surface, out var constraint) ? constraint : null;

    /// <summary>
    /// Destroys the driver, every live constraint and the global.
    /// </summary>
    public void Dispose()
    {
        Driver?.Dispose();
        var live = new PointerConstraint[_bySurface.Count];
        _bySurface.Values.CopyTo(live, 0);
        foreach (var constraint in live)
        {
            constraint.Teardown();
        }

        _bySurface.Clear();
        _global.Dispose();
    }

    internal Region? ResolveRegion(WlRegionResource? resource) => _compositor.ResolveRegion(resource);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpPointerConstraintsV1Resource(client, version, id);
        manager.LockPointer += (_, e) => Create(client, manager.Version, e.Id, e.Surface, e.Region, ConstraintKind.Lock, (uint)e.Lifetime);
        manager.ConfinePointer += (_, e) => Create(client, manager.Version, e.Id, e.Surface, e.Region, ConstraintKind.Confine, (uint)e.Lifetime);
    }

    private void Create(WlClient client, uint version, uint id, WlSurfaceResource? surfaceResource, WlRegionResource? region, ConstraintKind kind, uint lifetime)
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
                    (uint)ZwpPointerConstraintsV1.Error.AlreadyConstrained, "The surface already has a pointer constraint.");
            }
            else
            {
                new ZwpConfinedPointerV1Resource(client, version, id).PostError(
                    (uint)ZwpPointerConstraintsV1.Error.AlreadyConstrained, "The surface already has a pointer constraint.");
            }

            return;
        }

        var constraint = new PointerConstraint(this, surface, kind, lifetime == 2, _compositor.ResolveRegion(region), client, version, id);
        _bySurface[surface] = constraint;
        ConstraintCreated?.Invoke(constraint);
        Driver?.Refresh();
    }

    internal void NotifyActivated(PointerConstraint constraint) => ConstraintActivated?.Invoke(constraint);

    internal void Remove(PointerConstraint constraint)
    {
        if (_bySurface.TryGetValue(constraint.Surface, out var held) && held == constraint)
        {
            _bySurface.Remove(constraint.Surface);
        }

        ConstraintRemoved?.Invoke(constraint);
    }
}
