using Basin.Capabilities;

namespace Basin.Physics;

/// <summary>
/// Binds bodies to toplevels through an <see cref="IToplevelModel"/>. It resizes each body when the toplevel size changes, and it destroys the body when the toplevel goes away.
/// </summary>
public sealed class PhysicsWindows : IToplevelCommitObserver, IToplevelObserver, IDisposable
{
    private readonly IToplevelModel _model;
    private readonly Dictionary<ulong, Binding> _bindings = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhysicsWindows"/> class. It observes the model until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="model">The toplevel model that gives the sizes and the removals.</param>
    public PhysicsWindows(IToplevelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _model.AddObserver(this);
        _model.AddCommitObserver(this);
    }

    /// <summary>
    /// Gets the number of bindings, including bindings whose body is no longer valid.
    /// </summary>
    public int Count => _bindings.Count;

    /// <summary>
    /// Occurs when a binding ends, through <see cref="Unbind"/>, a new <see cref="Bind"/> for the same toplevel, or the removal of the toplevel. The arguments are the toplevel ID and the body, which is already destroyed.
    /// </summary>
    public event Action<ulong, PhysicsBody>? Unbound;

    /// <summary>
    /// Makes a body for a toplevel at its current size, plus the insets. It ends any earlier binding of the toplevel first. The body tag is the toplevel ID.
    /// </summary>
    /// <param name="world">The world that gets the body.</param>
    /// <param name="toplevelId">The toplevel to bind.</param>
    /// <param name="x">The left edge of the toplevel geometry, in logical pixels.</param>
    /// <param name="y">The top edge of the toplevel geometry, in logical pixels.</param>
    /// <param name="kind">The kind of the new body.</param>
    /// <param name="material">The material of the new body.</param>
    /// <param name="insets">The space that the body adds around the geometry.</param>
    /// <param name="awake">Whether the new body starts awake.</param>
    /// <returns>The new body, or <see langword="default"/> when the model does not know the toplevel.</returns>
    public PhysicsBody Bind(
        PhysicsWorld world,
        ulong toplevelId,
        double x,
        double y,
        PhysicsBodyKind kind,
        in PhysicsMaterial material,
        PhysicsInsets insets = default,
        bool awake = true)
    {
        ArgumentNullException.ThrowIfNull(world);
        Unbind(toplevelId);
        if (!_model.TryGet(toplevelId, out var info))
        {
            return default;
        }

        var (width, height) = SizeOf(info);
        var body = world.CreateBody(
            kind,
            x - insets.Left,
            y - insets.Top,
            width + insets.Horizontal,
            height + insets.Vertical,
            material,
            toplevelId,
            awake);
        _bindings[toplevelId] = new Binding(body, insets, width, height);
        return body;
    }

    /// <summary>
    /// Replaces the body of a binding, for example after <see cref="PhysicsWorld.MoveTo"/>. It sets the tag of the new body to the toplevel ID and does not destroy the old body.
    /// </summary>
    /// <param name="toplevelId">The bound toplevel.</param>
    /// <param name="body">The body that replaces the old one.</param>
    /// <returns><paramref name="body"/>, or <see langword="default"/> when the toplevel has no binding.</returns>
    public PhysicsBody Rebind(ulong toplevelId, PhysicsBody body)
    {
        if (!_bindings.TryGetValue(toplevelId, out var binding))
        {
            return default;
        }

        _bindings[toplevelId] = binding with { Body = body };
        body.Tag = toplevelId;
        return body;
    }

    /// <summary>
    /// Finds the body of a bound toplevel.
    /// </summary>
    /// <param name="toplevelId">The toplevel to look up.</param>
    /// <param name="body">The body, or <see langword="default"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the toplevel has a binding with a valid body.</returns>
    public bool TryGet(ulong toplevelId, out PhysicsBody body)
    {
        if (_bindings.TryGetValue(toplevelId, out var binding) && binding.Body.IsValid)
        {
            body = binding.Body;
            return true;
        }

        body = default;
        return false;
    }

    /// <summary>
    /// Gets the insets of a binding.
    /// </summary>
    /// <param name="toplevelId">The bound toplevel.</param>
    /// <returns>The insets, or <see langword="default"/> when the toplevel has no binding.</returns>
    public PhysicsInsets InsetsOf(ulong toplevelId) =>
        _bindings.TryGetValue(toplevelId, out var binding) ? binding.Insets : default;

    /// <summary>
    /// Changes the insets of a binding and resizes its body. When the body angle is zero, the geometry stays where it is on the screen.
    /// </summary>
    /// <param name="toplevelId">The bound toplevel. An unknown ID does nothing.</param>
    /// <param name="insets">The new insets.</param>
    public void SetInsets(ulong toplevelId, PhysicsInsets insets)
    {
        if (!_bindings.TryGetValue(toplevelId, out var binding) || binding.Insets == insets)
        {
            return;
        }

        _bindings[toplevelId] = binding with { Insets = insets };
        if (binding.Body.IsValid)
        {
            var pose = binding.Body.Current;
            binding.Body.Resize(binding.Width + insets.Horizontal, binding.Height + insets.Vertical);
            if (pose.Angle == 0)
            {
                binding.Body.Teleport(
                    pose.X + binding.Insets.Left - insets.Left, pose.Y + binding.Insets.Top - insets.Top);
            }
        }
    }

    /// <summary>
    /// Converts a body pose into the pose of the toplevel geometry origin, with the pivot moved to match the insets.
    /// </summary>
    /// <param name="toplevelId">The bound toplevel. An unknown ID uses zero insets.</param>
    /// <param name="pose">The body pose.</param>
    /// <returns>The pose of the geometry inside the body.</returns>
    public PhysicsPose ContentPose(ulong toplevelId, in PhysicsPose pose)
    {
        var insets = InsetsOf(toplevelId);
        return new PhysicsPose(pose.X + insets.Left, pose.Y + insets.Top, pose.Angle, pose.PivotX - insets.Left, pose.PivotY - insets.Top);
    }

    /// <summary>
    /// Ends a binding, destroys its body when it is still valid, and raises <see cref="Unbound"/>.
    /// </summary>
    /// <param name="toplevelId">The toplevel to unbind.</param>
    /// <returns><see langword="true"/> when the toplevel had a binding.</returns>
    public bool Unbind(ulong toplevelId)
    {
        if (!_bindings.Remove(toplevelId, out var binding))
        {
            return false;
        }

        if (binding.Body.IsValid)
        {
            binding.Body.Destroy();
        }

        Unbound?.Invoke(toplevelId, binding.Body);
        return true;
    }

    /// <summary>
    /// Stops observing the model and forgets every binding. It does not destroy the bodies and does not raise <see cref="Unbound"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.RemoveObserver(this);
        _model.RemoveCommitObserver(this);
        _bindings.Clear();
    }

    void IToplevelCommitObserver.OnToplevelCommitted(ulong toplevelId, in Box damage) => Track(toplevelId);

    void IToplevelObserver.OnToplevelAdded(ulong toplevelId)
    {
    }

    void IToplevelObserver.OnToplevelChanged(ulong toplevelId) => Track(toplevelId);

    void IToplevelObserver.OnToplevelRemoved(ulong toplevelId) => Unbind(toplevelId);

    private void Track(ulong toplevelId)
    {
        if (!_bindings.TryGetValue(toplevelId, out var binding) || !_model.TryGet(toplevelId, out var info))
        {
            return;
        }

        var (width, height) = SizeOf(info);
        if (width <= 0 || height <= 0 || (width == binding.Width && height == binding.Height))
        {
            return;
        }

        _bindings[toplevelId] = binding with { Width = width, Height = height };
        if (binding.Body.IsValid)
        {
            binding.Body.Resize(width + binding.Insets.Horizontal, height + binding.Insets.Vertical);
        }
    }

    private static (int Width, int Height) SizeOf(in ToplevelInfo info) =>
        !info.Geometry.IsEmpty ? (info.Geometry.Width, info.Geometry.Height)
        : info.Surface is { } surface ? (surface.Current.Width, surface.Current.Height)
        : (0, 0);

    private readonly record struct Binding(PhysicsBody Body, PhysicsInsets Insets, int Width, int Height);
}
