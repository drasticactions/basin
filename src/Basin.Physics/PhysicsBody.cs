namespace Basin.Physics;

/// <summary>
/// Refers to one body in a <see cref="PhysicsWorld"/>. The handle is stale after the body is destroyed. Each member except <see cref="IsValid"/> and <see cref="Destroy"/> then throws <see cref="InvalidOperationException"/>.
/// </summary>
/// <param name="World">The world that holds the body, or <see langword="null"/> for the default handle.</param>
/// <param name="Slot">The index of the body in the world.</param>
/// <param name="Generation">The generation of the slot, which makes a stale handle fail.</param>
public readonly record struct PhysicsBody(PhysicsWorld? World, int Slot, int Generation)
{
    /// <summary>
    /// Gets a value indicating whether the handle still refers to a live body in a world that is not disposed.
    /// </summary>
    public bool IsValid => World is { } world && world.IsLive(this);

    /// <summary>
    /// Gets or sets how the solver moves the body. A set during a drag takes effect when the drag ends. A set to a moving kind wakes the world.
    /// </summary>
    public PhysicsBodyKind Kind
    {
        get => Live.KindOf(this);
        set => Live.SetKind(this, value);
    }

    /// <summary>
    /// Gets or sets a consumer value stored with the body. <see cref="PhysicsWindows"/> stores the toplevel ID here.
    /// </summary>
    public ulong Tag
    {
        get => Live.TagOf(this);
        set => Live.SetTag(this, value);
    }

    /// <summary>
    /// Gets the box width in logical pixels. It is never less than 1.
    /// </summary>
    public double Width => Live.SizeOf(this).Width;

    /// <summary>
    /// Gets the box height in logical pixels. It is never less than 1.
    /// </summary>
    public double Height => Live.SizeOf(this).Height;

    /// <summary>
    /// Gets the pose to draw. It is the drag target during a drag, the rest pose for a sleeping body, and otherwise the blend of the last two steps.
    /// </summary>
    public PhysicsPose Pose => Live.Pose(this);

    /// <summary>
    /// Gets the pose at the end of the last step, with no blend and no drag target.
    /// </summary>
    public PhysicsPose Current => Live.CurrentPose(this);

    /// <summary>
    /// Gets or sets the linear velocity, in logical pixels per second. A set to a nonzero value wakes the body and the world.
    /// </summary>
    public (double X, double Y) Velocity
    {
        get => Live.VelocityOf(this);
        set => Live.SetVelocity(this, value.X, value.Y);
    }

    /// <summary>
    /// Gets or sets the angular velocity, in radians per second. A set to a nonzero value wakes the body and the world.
    /// </summary>
    public double AngularVelocity
    {
        get => Live.AngularVelocityOf(this);
        set => Live.SetAngularVelocity(this, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Locks"/> includes <see cref="PhysicsLocks.Rotation"/>. A lock keeps the current angle and does not straighten the body.
    /// </summary>
    public bool AngularLocked
    {
        get => (Live.LocksOf(this) & PhysicsLocks.Rotation) != 0;
        set => Live.SetLocks(this, value ? Live.LocksOf(this) | PhysicsLocks.Rotation : Live.LocksOf(this) & ~PhysicsLocks.Rotation);
    }

    /// <summary>
    /// Gets or sets the axes that the solver holds fixed for the body.
    /// </summary>
    public PhysicsLocks Locks
    {
        get => Live.LocksOf(this);
        set => Live.SetLocks(this, value);
    }

    /// <summary>
    /// Gets or sets the material of the body. A set applies to the engine body at once.
    /// </summary>
    public PhysicsMaterial Material
    {
        get => Live.MaterialOf(this);
        set => Live.SetMaterial(this, value);
    }

    /// <summary>
    /// Gets or sets the groups that the body collides with. A new body has <see cref="PhysicsCategory.All"/>.
    /// </summary>
    public PhysicsCategory CollidesWith
    {
        get => Live.MaskOf(this);
        set => Live.SetMask(this, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the body springs back to an angle of zero when its linear motion stops. A set to <see langword="false"/> cancels a spring in progress.
    /// </summary>
    public bool Upright
    {
        get => Live.UprightOf(this);
        set => Live.SetUpright(this, value);
    }

    /// <summary>
    /// Gets a value indicating whether the body moves, is dragged, or turns upright.
    /// </summary>
    public bool IsAwake => Live.IsBodyAwake(this);

    /// <summary>
    /// Gets a value indicating whether a drag holds the body, between <see cref="PhysicsWorld.BeginDrag"/> and <see cref="PhysicsWorld.EndDrag"/>.
    /// </summary>
    public bool IsDragging => Live.IsDragging(this);

    /// <summary>
    /// Gets a value indicating whether the upright spring turns the body back to an angle of zero.
    /// </summary>
    public bool IsUprighting => Live.IsUprighting(this);

    private PhysicsWorld Live => World ?? throw new InvalidOperationException("The body belongs to no world.");

    /// <summary>
    /// Moves the body with no sweep through the bodies between. It does not wake the body, and it cancels the upright spring.
    /// </summary>
    /// <param name="x">The new left edge of the unrotated box, in logical pixels.</param>
    /// <param name="y">The new top edge of the unrotated box, in logical pixels.</param>
    /// <param name="angle">The new angle in radians.</param>
    public void Teleport(double x, double y, double angle = 0) => Live.Teleport(this, x, y, angle);

    /// <summary>
    /// Changes the box size and recomputes the mass from the new area. The top-left stays where it was in the body frame.
    /// </summary>
    /// <param name="width">The new width in logical pixels. A value below 1 becomes 1.</param>
    /// <param name="height">The new height in logical pixels. A value below 1 becomes 1.</param>
    public void Resize(double width, double height) => Live.Resize(this, width, height);

    /// <summary>
    /// Removes the body from its world. It does nothing for a stale or default handle.
    /// </summary>
    public void Destroy() => World?.Destroy(this);
}
