using Basin.Diagnostics;
using Basin.Kinematics;

namespace Basin.Physics;

/// <summary>
/// Simulates boxes in a plane on one <see cref="PhysicsEngine"/>, in logical pixels with the y axis down. The consumer steps it on each frame tick and writes the poses back. The world never schedules a frame.
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    /// <summary>
    /// The fixed scale between logical pixels and engine meters.
    /// </summary>
    public const double PixelsPerMeter = 100.0;

    /// <summary>
    /// The fixed step length, in nanoseconds. It does not depend on the display refresh rate.
    /// </summary>
    public const long StepNanos = 16_666_667;

    /// <summary>
    /// The most steps that one <see cref="Step"/> call runs. A larger backlog is dropped.
    /// </summary>
    public const int MaxStepsPerTick = 2;

    /// <summary>
    /// The number of engine substeps in each step.
    /// </summary>
    public const int SubSteps = 4;

    private const double StepSeconds = 1.0 / 60.0;

    private const double RestAngle = 1e-4;

    private const double UprightEpsilon = 0.0005;

    private const double PenetrationMeters = 0.02;

    private readonly IPhysicsBackend _backend;
    private Slot[] _slots = new Slot[32];
    private int[] _free = new int[32];
    private int _freeCount;
    private int _slotCount;
    private PhysicsBody[] _moved = new PhysicsBody[32];
    private int _movedCount;
    private PhysicsImpact[] _impacts = new PhysicsImpact[32];
    private int _impactCount;
    private TickDelta _clock;
    private long _accumulator;
    private double _gravityX;
    private double _gravityY;
    private bool _awake;
    private bool _frozen;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhysicsWorld"/> class on an engine. The world owns its engine state until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="engine">The engine to run on.</param>
    /// <param name="gravityX">The horizontal gravity, in logical pixels per second squared.</param>
    /// <param name="gravityY">The vertical gravity, in logical pixels per second squared. A positive value pulls down.</param>
    public PhysicsWorld(PhysicsEngine engine, double gravityX = 0, double gravityY = 0)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        _backend = engine.CreateBackend(gravityX / PixelsPerMeter, gravityY / PixelsPerMeter, ImpactThreshold / PixelsPerMeter);
        _gravityX = gravityX;
        _gravityY = gravityY;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets or sets the approach speed, in logical pixels per second, above which a new contact becomes a <see cref="PhysicsImpact"/>. A negative value becomes 0. The default is 150.
    /// </summary>
    public double ImpactThreshold
    {
        get;
        set
        {
            field = Math.Max(0, value);
            if (!_disposed)
            {
                _backend.SetImpactThreshold(field / PixelsPerMeter);
            }
        }
    } = 150;

    /// <summary>
    /// Gets the engine that the world runs on. A body can move only to a world on the same engine.
    /// </summary>
    public PhysicsEngine Engine { get; }

    /// <summary>
    /// Gets or sets the factor that <see cref="EndDrag"/> applies to the release velocity.
    /// </summary>
    public double ThrowMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the highest speed that <see cref="EndDrag"/> gives a body, in logical pixels per second, after <see cref="ThrowMultiplier"/>.
    /// </summary>
    public double MaxThrowSpeed { get; set; } = 6000;

    /// <summary>
    /// Gets or sets the speed in logical pixels per second below which a slowing dynamic body stops and sleeps. It applies only to a body with no gravity.
    /// </summary>
    public double StopSpeed { get; set; } = 20;

    /// <summary>
    /// Gets or sets the drag jump, in logical pixels, at or above which <see cref="Drag(PhysicsBody, double, double)"/> places the body directly. Such a jump does not push the bodies between.
    /// </summary>
    public double TeleportDistance { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Gets or sets the factor on the spin that an off-center throw gives. Zero gives no spin.
    /// </summary>
    public double SpinScale { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the stiffness of the spring that turns an upright body back to an angle of zero. A change applies to the next spring that starts.
    /// </summary>
    public double UprightStiffness { get; set; } = 200;

    /// <summary>
    /// Gets or sets the gravity, in logical pixels per second squared. A set wakes every dynamic body.
    /// </summary>
    public (double X, double Y) Gravity
    {
        get => (_gravityX, _gravityY);
        set
        {
            _gravityX = value.X;
            _gravityY = value.Y;
            _backend.SetGravity(value.X / PixelsPerMeter, value.Y / PixelsPerMeter);
            WakeAll();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Step"/> does nothing. A frozen world keeps its accumulator, so the poses stay the poses drawn last.
    /// </summary>
    public bool Frozen
    {
        get => _frozen;
        set
        {
            if (_frozen == value)
            {
                return;
            }

            _frozen = value;
            _clock.Reset();
            if (!value)
            {
                RefreshAwake();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a body moves, is dragged or turns upright, and the world is not frozen. The consumer schedules a repaint while it is <see langword="true"/>.
    /// </summary>
    public bool IsAwake => !_frozen && _awake;

    /// <summary>
    /// Gets the number of live bodies, wall bodies included.
    /// </summary>
    public int Count => _slotCount - _freeCount;

    /// <summary>
    /// Gets the bodies whose <see cref="PhysicsBody.Pose"/> changed in the last <see cref="Step"/>. The span is valid until the next <see cref="Step"/>.
    /// </summary>
    public ReadOnlySpan<PhysicsBody> Moved => _moved.AsSpan(0, _movedCount);

    /// <summary>
    /// Gets the impacts of the last <see cref="Step"/>. The span is valid until the next <see cref="Step"/>.
    /// </summary>
    public ReadOnlySpan<PhysicsImpact> Impacts => _impacts.AsSpan(0, _impactCount);

    /// <summary>
    /// Adds a box body with the mask <see cref="PhysicsCategory.All"/> and no locks.
    /// </summary>
    /// <param name="kind">The kind of the body.</param>
    /// <param name="x">The left edge of the box, in logical pixels.</param>
    /// <param name="y">The top edge of the box, in logical pixels.</param>
    /// <param name="width">The box width. A value below 1 becomes 1.</param>
    /// <param name="height">The box height. A value below 1 becomes 1.</param>
    /// <param name="material">The material of the body.</param>
    /// <param name="tag">The consumer value stored with the body.</param>
    /// <param name="awake">Whether the body starts awake. A body made asleep does not move or push until something wakes it.</param>
    /// <returns>The handle of the new body.</returns>
    /// <exception cref="ObjectDisposedException">The world is disposed.</exception>
    public PhysicsBody CreateBody(
        PhysicsBodyKind kind,
        double x,
        double y,
        double width,
        double height,
        in PhysicsMaterial material,
        ulong tag = 0,
        bool awake = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var index = AllocateSlot();
        ref var slot = ref _slots[index];

        _backend.CreateBody(
            index,
            kind,
            (x + (width / 2)) / PixelsPerMeter,
            (y + (height / 2)) / PixelsPerMeter,
            width / 2 / PixelsPerMeter,
            height / 2 / PixelsPerMeter,
            material,
            awake);

        slot.Live = true;
        slot.Kind = kind;
        slot.Width = width;
        slot.Height = height;
        slot.Material = material;
        slot.Tag = tag;
        slot.Mask = PhysicsCategory.All;
        slot.Locks = PhysicsLocks.None;
        slot.Upright = false;
        slot.Dragging = false;
        slot.Uprighting = false;
        slot.Asleep = kind == PhysicsBodyKind.Static || !awake;
        slot.CenterX = slot.PrevX = x + (width / 2);
        slot.CenterY = slot.PrevY = y + (height / 2);
        slot.Angle = slot.PrevAngle = 0;
        slot.Reported = default;
        slot.PrevSpeed = 0;
        slot.Wall = false;
        _awake |= kind != PhysicsBodyKind.Static && awake;
        return new PhysicsBody(this, index, slot.Generation);
    }

    /// <summary>
    /// Removes a body. Every handle to it becomes stale. It does nothing for a handle that is not live in this world.
    /// </summary>
    /// <param name="body">The body to remove.</param>
    public void Destroy(PhysicsBody body)
    {
        if (!IsLive(body))
        {
            return;
        }

        ref var slot = ref _slots[body.Slot];
        _backend.DestroyBody(body.Slot);
        slot.Live = false;
        slot.Generation++;
        _free[_freeCount++] = body.Slot;
    }

    /// <summary>
    /// Moves a body to another world. It makes a copy with the same size, kind, material, tag, locks, mask and upright flag, and then destroys the original.
    /// </summary>
    /// <param name="body">The body to move.</param>
    /// <param name="target">The world that gets the copy. It can be on another engine, because the copy keeps only the listed state and the velocity.</param>
    /// <param name="x">The left edge of the copy in the target world.</param>
    /// <param name="y">The top edge of the copy in the target world.</param>
    /// <param name="velocityScale">The factor on the velocity of a dynamic body. Zero places the copy at rest and asleep.</param>
    /// <returns>The handle of the copy, or <see langword="default"/> when <paramref name="body"/> is not live in this world.</returns>
    public PhysicsBody MoveTo(PhysicsBody body, PhysicsWorld target, double x, double y, double velocityScale)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsLive(body))
        {
            return default;
        }

        ref var slot = ref _slots[body.Slot];
        var (vx, vy) = VelocityOf(body);
        var spin = AngularVelocityOf(body);
        var moving = velocityScale != 0 && ((vx * vx) + (vy * vy) > 0 || spin != 0);
        var moved = target.CreateBody(slot.Kind, x, y, slot.Width, slot.Height, slot.Material, slot.Tag, awake: moving);
        target.SetLocks(moved, slot.Locks);
        target.SetMask(moved, slot.Mask);
        target.SetUpright(moved, slot.Upright);
        if (velocityScale != 0 && slot.Kind == PhysicsBodyKind.Dynamic)
        {
            target.SetVelocity(moved, vx * velocityScale, vy * velocityScale);
            if ((slot.Locks & PhysicsLocks.Rotation) == 0)
            {
                target.SetAngularVelocity(moved, spin);
            }
        }

        Destroy(body);
        return moved;
    }

    /// <summary>
    /// Advances the world to a frame tick with fixed steps and an accumulator. A tick that does not advance past the last one steps nothing. The first tick after a wake counts one refresh interval.
    /// </summary>
    /// <param name="tick">The frame tick of the output that repaints.</param>
    /// <returns><see langword="true"/> while the world is still awake and needs another frame.</returns>
    public bool Step(in FrameTick tick)
    {
        _movedCount = 0;
        _impactCount = 0;
        if (_frozen || _disposed)
        {
            return false;
        }

        if (!_awake)
        {
            _clock.Reset();
            _accumulator = 0;
            return false;
        }

        var elapsed = _clock.Advance(tick);
        if (elapsed <= 0)
        {
            return true;
        }

        _accumulator += elapsed;
        var steps = 0;
        while (_accumulator >= StepNanos && steps < MaxStepsPerTick)
        {
            SubStep();
            _accumulator -= StepNanos;
            steps++;
        }

        if (_accumulator >= StepNanos)
        {
            _accumulator %= StepNanos;
        }

        RefreshAwake();
        CollectMoved();
        if (!_awake)
        {
            _clock.Reset();
            _accumulator = 0;
        }

        return _awake;
    }

    /// <summary>
    /// Releases the engine state. After it, <see cref="Step"/> does nothing and every body handle is stale.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.Dispose();
        BasinCounters.Untrack();
    }

    /// <summary>
    /// Starts a drag. The body becomes kinematic until <see cref="EndDrag"/>, and the grab point stays fixed in the body frame. The angle does not change during the drag.
    /// </summary>
    /// <param name="body">The body to drag. A handle that is not live does nothing.</param>
    /// <param name="grabX">The horizontal grab position, in logical pixels.</param>
    /// <param name="grabY">The vertical grab position, in logical pixels.</param>
    public void BeginDrag(PhysicsBody body, double grabX, double grabY)
    {
        if (!IsLive(body))
        {
            return;
        }

        ref var slot = ref _slots[body.Slot];
        slot.Uprighting = false;
        slot.KindBeforeDrag = slot.Dragging ? slot.KindBeforeDrag : slot.Kind;
        var cos = Math.Cos(-slot.Angle);
        var sin = Math.Sin(-slot.Angle);
        var dx = grabX - slot.CenterX;
        var dy = grabY - slot.CenterY;
        slot.GrabLocalX = (cos * dx) - (sin * dy);
        slot.GrabLocalY = (sin * dx) + (cos * dy);
        slot.DragX = grabX;
        slot.DragY = grabY;
        slot.DragAngle = double.NaN;
        slot.Dragging = true;
        slot.Asleep = false;
        SetType(body.Slot, PhysicsBodyKind.Kinematic);
        _backend.SetVelocity(body.Slot, 0, 0);
        _backend.SetAngularVelocity(body.Slot, 0);
        _backend.SetAwake(body.Slot, true);
        _awake = true;
    }

    /// <summary>
    /// Moves the drag target of a dragged body and keeps its angle. The body reaches the target over the next step and pushes the dynamic bodies in its way.
    /// </summary>
    /// <param name="body">The dragged body. A body that is not dragged does nothing.</param>
    /// <param name="x">The new horizontal grab position.</param>
    /// <param name="y">The new vertical grab position.</param>
    public void Drag(PhysicsBody body, double x, double y) => Drag(body, x, y, double.NaN);

    /// <summary>
    /// Moves the drag target of a dragged body and turns it about the grab point.
    /// </summary>
    /// <param name="body">The dragged body. A body that is not dragged does nothing.</param>
    /// <param name="x">The new horizontal grab position.</param>
    /// <param name="y">The new vertical grab position.</param>
    /// <param name="angle">The target angle in radians, or <see cref="double.NaN"/> to keep the angle.</param>
    public void Drag(PhysicsBody body, double x, double y, double angle)
    {
        if (!IsLive(body) || !_slots[body.Slot].Dragging)
        {
            return;
        }

        ref var slot = ref _slots[body.Slot];
        slot.DragAngle = angle;
        slot.DragX = x;
        slot.DragY = y;
        var (targetX, targetY) = DragCenter(slot);
        var dx = targetX - slot.CenterX;
        var dy = targetY - slot.CenterY;
        if ((dx * dx) + (dy * dy) >= TeleportDistance * TeleportDistance)
        {
            Place(body.Slot, targetX, targetY, slot.Angle);
        }

        _awake = true;
    }

    /// <summary>
    /// Ends a drag at the last target and restores the kind of the body. A dynamic body gets the release velocity and, for an off-center grab, a spin.
    /// </summary>
    /// <param name="body">The dragged body. A body that is not dragged does nothing.</param>
    /// <param name="velocityX">The horizontal release velocity, in logical pixels per second.</param>
    /// <param name="velocityY">The vertical release velocity, in logical pixels per second.</param>
    public void EndDrag(PhysicsBody body, double velocityX, double velocityY)
    {
        if (!IsLive(body) || !_slots[body.Slot].Dragging)
        {
            return;
        }

        ref var slot = ref _slots[body.Slot];
        var (targetX, targetY) = DragCenter(slot);
        Place(body.Slot, targetX, targetY, DragAngleOf(slot));
        slot.Dragging = false;
        SetType(body.Slot, slot.KindBeforeDrag);
        if (slot.KindBeforeDrag != PhysicsBodyKind.Dynamic)
        {
            return;
        }

        var vx = velocityX * ThrowMultiplier;
        var vy = velocityY * ThrowMultiplier;
        var speed = Math.Sqrt((vx * vx) + (vy * vy));
        if (speed > MaxThrowSpeed && speed > 0)
        {
            vx *= MaxThrowSpeed / speed;
            vy *= MaxThrowSpeed / speed;
        }

        _backend.SetVelocity(body.Slot, vx / PixelsPerMeter, vy / PixelsPerMeter);
        if ((slot.Locks & PhysicsLocks.Rotation) == 0 && SpinScale != 0)
        {
            var cos = Math.Cos(slot.Angle);
            var sin = Math.Sin(slot.Angle);
            var rx = (cos * slot.GrabLocalX) - (sin * slot.GrabLocalY);
            var ry = (sin * slot.GrabLocalX) + (cos * slot.GrabLocalY);
            var inertia = ((slot.Width * slot.Width) + (slot.Height * slot.Height)) / 12;
            var spin = SpinScale * ((rx * vy) - (ry * vx)) / inertia;
            _backend.SetAngularVelocity(body.Slot, spin);
        }

        slot.PrevSpeed = double.MaxValue;
        _awake = true;
    }

    internal bool IsLive(PhysicsBody body) =>
        ReferenceEquals(body.World, this) && !_disposed &&
        (uint)body.Slot < (uint)_slotCount && _slots[body.Slot].Live && _slots[body.Slot].Generation == body.Generation;

    internal PhysicsBodyKind KindOf(PhysicsBody body) => Checked(body).Kind;

    internal void SetKind(PhysicsBody body, PhysicsBodyKind kind)
    {
        ref var slot = ref Checked(body);
        if (slot.Dragging)
        {
            slot.KindBeforeDrag = kind;
            return;
        }

        slot.Uprighting = false;
        SetType(body.Slot, kind);
        slot.Asleep = kind == PhysicsBodyKind.Static;
        _awake |= kind != PhysicsBodyKind.Static;
    }

    internal ulong TagOf(PhysicsBody body) => Checked(body).Tag;

    internal void SetTag(PhysicsBody body, ulong tag) => Checked(body).Tag = tag;

    internal (double Width, double Height) SizeOf(PhysicsBody body)
    {
        ref var slot = ref Checked(body);
        return (slot.Width, slot.Height);
    }

    internal PhysicsPose Pose(PhysicsBody body)
    {
        ref var slot = ref Checked(body);
        return PoseOf(ref slot);
    }

    internal PhysicsPose CurrentPose(PhysicsBody body)
    {
        ref var slot = ref Checked(body);
        return new PhysicsPose(
            slot.CenterX - (slot.Width / 2), slot.CenterY - (slot.Height / 2), slot.Angle, slot.Width / 2, slot.Height / 2);
    }

    internal (double X, double Y) VelocityOf(PhysicsBody body)
    {
        Checked(body);
        var (x, y) = _backend.VelocityOf(body.Slot);
        return (x * PixelsPerMeter, y * PixelsPerMeter);
    }

    internal void SetVelocity(PhysicsBody body, double x, double y)
    {
        ref var slot = ref Checked(body);
        slot.Uprighting = slot.Uprighting && x == 0 && y == 0;
        _backend.SetVelocity(body.Slot, x / PixelsPerMeter, y / PixelsPerMeter);
        slot.PrevSpeed = double.MaxValue;
        if (x != 0 || y != 0)
        {
            slot.Asleep = false;
            _awake = true;
        }
    }

    internal double AngularVelocityOf(PhysicsBody body)
    {
        Checked(body);
        return _backend.AngularVelocityOf(body.Slot);
    }

    internal void SetAngularVelocity(PhysicsBody body, double radiansPerSecond)
    {
        ref var slot = ref Checked(body);
        _backend.SetAngularVelocity(body.Slot, radiansPerSecond);
        if (radiansPerSecond != 0)
        {
            slot.Asleep = false;
            _awake = true;
        }
    }

    internal PhysicsLocks LocksOf(PhysicsBody body) => Checked(body).Locks;

    internal void SetLocks(PhysicsBody body, PhysicsLocks locks)
    {
        ref var slot = ref Checked(body);
        slot.Locks = locks;
        _backend.SetLocks(body.Slot, locks);
    }

    internal PhysicsMaterial MaterialOf(PhysicsBody body) => Checked(body).Material;

    internal void SetMaterial(PhysicsBody body, in PhysicsMaterial material)
    {
        ref var slot = ref Checked(body);
        slot.Material = material;
        _backend.SetMaterial(body.Slot, material);
    }

    internal PhysicsCategory MaskOf(PhysicsBody body) => Checked(body).Mask;

    internal void SetMask(PhysicsBody body, PhysicsCategory mask)
    {
        ref var slot = ref Checked(body);
        slot.Mask = mask;
        _backend.SetMask(body.Slot, mask);
    }

    internal bool UprightOf(PhysicsBody body) => Checked(body).Upright;

    internal void SetUpright(PhysicsBody body, bool upright)
    {
        ref var slot = ref Checked(body);
        slot.Upright = upright;
        if (!upright)
        {
            slot.Uprighting = false;
        }
        else if (slot.Asleep && slot.Angle != 0)
        {
            slot.Asleep = false;
            _backend.SetAwake(body.Slot, true);
            _awake = true;
        }
    }

    internal bool IsBodyAwake(PhysicsBody body)
    {
        ref var slot = ref Checked(body);
        return !slot.Asleep || slot.Dragging || slot.Uprighting;
    }

    internal bool IsDragging(PhysicsBody body) => Checked(body).Dragging;

    internal bool IsUprighting(PhysicsBody body) => Checked(body).Uprighting;

    internal void Teleport(PhysicsBody body, double x, double y, double angle)
    {
        ref var slot = ref Checked(body);
        slot.Uprighting = false;
        Place(body.Slot, x + (slot.Width / 2), y + (slot.Height / 2), angle);
    }

    internal void Resize(PhysicsBody body, double width, double height)
    {
        ref var slot = ref Checked(body);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == slot.Width && height == slot.Height)
        {
            return;
        }

        var cos = Math.Cos(slot.Angle);
        var sin = Math.Sin(slot.Angle);
        var growX = (width - slot.Width) / 2;
        var growY = (height - slot.Height) / 2;
        var centerX = slot.CenterX + (cos * growX) - (sin * growY);
        var centerY = slot.CenterY + (sin * growX) + (cos * growY);
        _backend.SetBox(body.Slot, width / 2 / PixelsPerMeter, height / 2 / PixelsPerMeter);
        slot.Width = width;
        slot.Height = height;
        Place(body.Slot, centerX, centerY, slot.Angle);
    }

    internal double MassOf(PhysicsBody body)
    {
        Checked(body);
        return _backend.MassOf(body.Slot);
    }

    internal PhysicsBody CreateWall()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var index = AllocateSlot();
        ref var slot = ref _slots[index];
        _backend.CreateWall(index);
        slot.Live = true;
        slot.Wall = true;
        slot.Kind = PhysicsBodyKind.Static;
        slot.Asleep = true;
        slot.Width = slot.Height = 0;
        slot.CenterX = slot.CenterY = slot.PrevX = slot.PrevY = 0;
        slot.Angle = slot.PrevAngle = 0;
        slot.Dragging = slot.Uprighting = false;
        slot.Tag = 0;
        return new PhysicsBody(this, index, slot.Generation);
    }

    internal void AddWallBox(PhysicsBody wall, in Box box, PhysicsCategory category)
    {
        Checked(wall);
        _backend.AddWallBox(
            wall.Slot,
            (box.X + (box.Width / 2.0)) / PixelsPerMeter,
            (box.Y + (box.Height / 2.0)) / PixelsPerMeter,
            box.Width / 2.0 / PixelsPerMeter,
            box.Height / 2.0 / PixelsPerMeter,
            category);
    }

    private ref Slot Checked(PhysicsBody body)
    {
        if (!IsLive(body))
        {
            throw new InvalidOperationException("The physics body is not live in this world.");
        }

        return ref _slots[body.Slot];
    }

    private int AllocateSlot()
    {
        if (_freeCount > 0)
        {
            return _free[--_freeCount];
        }

        if (_slotCount == _slots.Length)
        {
            Array.Resize(ref _slots, _slots.Length * 2);
            Array.Resize(ref _free, _free.Length * 2);
            Array.Resize(ref _moved, _moved.Length * 2);
        }

        return _slotCount++;
    }

    private void SubStep()
    {
        for (var i = 0; i < _slotCount; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Wall)
            {
                continue;
            }

            slot.PrevX = slot.CenterX;
            slot.PrevY = slot.CenterY;
            slot.PrevAngle = slot.Angle;
            if (slot.Dragging)
            {
                var (x, y) = DragCenter(slot);
                Target(i, x, y, DragAngleOf(slot));
            }
            else if (slot.Uprighting)
            {
                slot.UprightSpring.Advance(StepNanos);
                Target(i, slot.CenterX, slot.CenterY, slot.UprightSpring.X);
            }
        }

        _backend.Step(StepSeconds, SubSteps);

        CollectImpacts();
        var still = _gravityX == 0 && _gravityY == 0;
        for (var i = 0; i < _slotCount; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Wall || slot.Kind == PhysicsBodyKind.Static && !slot.Dragging)
            {
                continue;
            }

            var awake = _backend.IsAwake(i);
            if (awake)
            {
                _backend.ReadPose(i, out var x, out var y, out var angle);
                slot.CenterX = x * PixelsPerMeter;
                slot.CenterY = y * PixelsPerMeter;
                slot.Angle = angle;
            }

            if (slot.Dragging)
            {
                slot.Asleep = false;
                continue;
            }

            if (slot.Uprighting)
            {
                if (!slot.UprightSpring.IsMoving || Math.Abs(slot.UprightSpring.X) < UprightEpsilon && Math.Abs(slot.UprightSpring.VelocityX) < UprightEpsilon)
                {
                    FinishUpright(i);
                }

                continue;
            }

            if (awake && slot.Kind == PhysicsBodyKind.Dynamic)
            {
                var (vx, vy) = _backend.VelocityOf(i);
                var speed = Math.Sqrt((vx * vx) + (vy * vy)) * PixelsPerMeter;
                var spin = Math.Abs(_backend.AngularVelocityOf(i));
                var gravity = !still && slot.Material.GravityScale != 0;
                var slowing = speed < StopSpeed && speed <= slot.PrevSpeed;
                slot.PrevSpeed = speed;
                if (slowing && slot.Upright && Math.Abs(slot.Angle) > RestAngle && !gravity)
                {
                    StartUpright(i);
                    continue;
                }

                if (slowing && spin < 0.2 && !gravity && !_backend.Penetrating(i, PenetrationMeters))
                {
                    _backend.SetVelocity(i, 0, 0);
                    _backend.SetAngularVelocity(i, 0);
                    _backend.SetAwake(i, false);
                    awake = false;
                }
            }

            if (!awake && !slot.Asleep)
            {
                if (slot.Upright && Math.Abs(slot.Angle) > RestAngle && slot.Kind == PhysicsBodyKind.Dynamic)
                {
                    StartUpright(i);
                    continue;
                }

                Rest(i);
            }
            else if (awake)
            {
                slot.Asleep = false;
            }
        }
    }

    private void StartUpright(int index)
    {
        ref var slot = ref _slots[index];
        var spin = _backend.AngularVelocityOf(index);
        slot.Uprighting = true;
        SetType(index, PhysicsBodyKind.Kinematic);
        _backend.SetVelocity(index, 0, 0);
        _backend.SetAngularVelocity(index, 0);
        _backend.SetAwake(index, true);
        slot.UprightSpring = new SpringHandoff(UprightStiffness, 1.0) { Epsilon = UprightEpsilon };
        slot.UprightSpring.Start(slot.Angle, 0, spin, 0, 0, 0);
        slot.Uprighting = true;
        slot.Asleep = false;
    }

    private void FinishUpright(int index)
    {
        ref var slot = ref _slots[index];
        slot.Uprighting = false;
        SetType(index, slot.Kind);
        Place(index, slot.CenterX, slot.CenterY, 0);
        _backend.SetVelocity(index, 0, 0);
        _backend.SetAngularVelocity(index, 0);
        _backend.SetAwake(index, false);
        Rest(index);
    }

    private void Rest(int index)
    {
        ref var slot = ref _slots[index];
        var angle = Math.Abs(slot.Angle) < RestAngle ? 0 : slot.Angle;
        var x = Math.Round(slot.CenterX - (slot.Width / 2)) + (slot.Width / 2);
        var y = Math.Round(slot.CenterY - (slot.Height / 2)) + (slot.Height / 2);
        Place(index, x, y, angle);
        slot.Asleep = true;
        slot.PrevSpeed = 0;
    }

    private void Place(int index, double centerX, double centerY, double angle)
    {
        ref var slot = ref _slots[index];
        _backend.SetTransform(index, centerX / PixelsPerMeter, centerY / PixelsPerMeter, angle);
        slot.CenterX = slot.PrevX = centerX;
        slot.CenterY = slot.PrevY = centerY;
        slot.Angle = slot.PrevAngle = angle;
    }

    private void Target(int index, double centerX, double centerY, double angle) =>
        _backend.SetTarget(index, centerX / PixelsPerMeter, centerY / PixelsPerMeter, angle, StepSeconds);

    private static double DragAngleOf(in Slot slot) => double.IsNaN(slot.DragAngle) ? slot.Angle : slot.DragAngle;

    private static (double X, double Y) DragCenter(in Slot slot)
    {
        var angle = DragAngleOf(slot);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return (
            slot.DragX - ((cos * slot.GrabLocalX) - (sin * slot.GrabLocalY)),
            slot.DragY - ((sin * slot.GrabLocalX) + (cos * slot.GrabLocalY)));
    }

    private void SetType(int index, PhysicsBodyKind kind)
    {
        ref var slot = ref _slots[index];
        if (!slot.Dragging && !slot.Uprighting)
        {
            slot.Kind = kind;
        }

        _backend.SetKind(index, kind, slot.Locks);
    }

    private PhysicsPose PoseOf(ref Slot slot)
    {
        double x, y, angle;
        if (slot.Dragging)
        {
            (x, y) = DragCenter(slot);
            angle = DragAngleOf(slot);
        }
        else if (slot.Asleep)
        {
            (x, y, angle) = (slot.CenterX, slot.CenterY, slot.Angle);
        }
        else
        {
            var alpha = (double)_accumulator / StepNanos;
            x = slot.PrevX + ((slot.CenterX - slot.PrevX) * alpha);
            y = slot.PrevY + ((slot.CenterY - slot.PrevY) * alpha);
            angle = slot.PrevAngle + (Math.IEEERemainder(slot.Angle - slot.PrevAngle, Math.Tau) * alpha);
        }

        return new PhysicsPose(x - (slot.Width / 2), y - (slot.Height / 2), angle, slot.Width / 2, slot.Height / 2);
    }

    private void CollectImpacts()
    {
        foreach (ref readonly var hit in _backend.Hits)
        {
            var a = BodyOf(hit.SlotA);
            var b = BodyOf(hit.SlotB);
            if (_impactCount == _impacts.Length)
            {
                Array.Resize(ref _impacts, _impacts.Length * 2);
            }

            _impacts[_impactCount++] = new PhysicsImpact(
                a,
                b,
                hit.X * PixelsPerMeter,
                hit.Y * PixelsPerMeter,
                hit.NormalX,
                hit.NormalY,
                hit.Speed * PixelsPerMeter);
        }
    }

    private PhysicsBody BodyOf(int index) =>
        (uint)index < (uint)_slotCount && _slots[index].Live
            ? new PhysicsBody(this, index, _slots[index].Generation)
            : default;

    private void CollectMoved()
    {
        for (var i = 0; i < _slotCount; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Wall)
            {
                continue;
            }

            var pose = PoseOf(ref slot);
            if (pose == slot.Reported)
            {
                continue;
            }

            slot.Reported = pose;
            _moved[_movedCount++] = new PhysicsBody(this, i, slot.Generation);
        }
    }

    private void RefreshAwake()
    {
        var awake = false;
        for (var i = 0; i < _slotCount && !awake; i++)
        {
            ref var slot = ref _slots[i];
            awake = slot.Live && !slot.Wall && (slot.Dragging || slot.Uprighting || !slot.Asleep);
        }

        _awake = awake;
    }

    private void WakeAll()
    {
        for (var i = 0; i < _slotCount; i++)
        {
            ref var slot = ref _slots[i];
            if (slot.Live && !slot.Wall && slot.Kind == PhysicsBodyKind.Dynamic)
            {
                _backend.SetAwake(i, true);
                slot.Asleep = false;
                slot.PrevSpeed = double.MaxValue;
                _awake = true;
            }
        }
    }

    private struct Slot
    {
        public int Generation;
        public bool Live;
        public bool Wall;
        public PhysicsBodyKind Kind;
        public PhysicsBodyKind KindBeforeDrag;
        public double Width;
        public double Height;
        public PhysicsMaterial Material;
        public ulong Tag;
        public PhysicsCategory Mask;
        public PhysicsLocks Locks;
        public bool Upright;
        public bool Uprighting;
        public SpringHandoff UprightSpring;
        public bool Dragging;
        public double GrabLocalX;
        public double GrabLocalY;
        public double DragX;
        public double DragY;
        public double DragAngle;
        public bool Asleep;
        public double CenterX;
        public double CenterY;
        public double Angle;
        public double PrevX;
        public double PrevY;
        public double PrevAngle;
        public double PrevSpeed;
        public PhysicsPose Reported;
    }
}
