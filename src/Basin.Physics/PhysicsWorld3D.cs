using System.Numerics;
using Basin.Diagnostics;
using Basin.Kinematics;

namespace Basin.Physics;

/// <summary>
/// Simulates boxes, cylinders and capsule characters in 3D on one <see cref="PhysicsEngine3D"/>, in meters. It shares the fixed step and the idle rule of <see cref="PhysicsWorld"/>, and it never schedules a frame.
/// </summary>
public sealed class PhysicsWorld3D : IDisposable
{
    /// <summary>
    /// The fixed step length, in nanoseconds.
    /// </summary>
    public const long StepNanos = PhysicsWorld.StepNanos;

    /// <summary>
    /// The most steps that one <see cref="Step"/> call runs. A larger backlog is dropped.
    /// </summary>
    public const int MaxStepsPerTick = PhysicsWorld.MaxStepsPerTick;

    private const float StepSeconds = 1f / 60f;

    private const int RestSteps = 20;

    private readonly IPhysicsBackend3D _backend;
    private Slot[] _slots = new Slot[32];
    private int[] _free = new int[32];
    private PhysicsBody3D[] _moved = new PhysicsBody3D[32];
    private PhysicsImpact3D[] _impacts = new PhysicsImpact3D[16];
    private int _slotCount;
    private int _freeCount;
    private int _movedCount;
    private int _impactCount;
    private TickDelta _clock;
    private long _accumulator;
    private Vector3 _gravity;
    private float _impactThreshold = 1.5f;
    private bool _frozen;
    private bool _awake;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhysicsWorld3D"/> class on an engine. The world owns its engine state until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="engine">The engine to run on.</param>
    /// <param name="gravity">The gravity, in meters per second squared.</param>
    public PhysicsWorld3D(PhysicsEngine3D engine, Vector3 gravity)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        _gravity = gravity;
        _backend = engine.CreateBackend(gravity, _impactThreshold);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the engine that the world runs on.
    /// </summary>
    public PhysicsEngine3D Engine { get; }

    /// <summary>
    /// Gets or sets the gravity, in meters per second squared. A set wakes every body.
    /// </summary>
    public Vector3 Gravity
    {
        get => _gravity;
        set
        {
            _gravity = value;
            _backend.SetGravity(value);
            WakeAll();
        }
    }

    /// <summary>
    /// Gets or sets the approach speed, in meters per second, above which a new contact becomes a <see cref="PhysicsImpact3D"/>. A negative value becomes 0. The default is 1.5.
    /// </summary>
    public double ImpactThreshold
    {
        get => _impactThreshold;
        set
        {
            _impactThreshold = (float)Math.Max(0, value);
            _backend.SetImpactThreshold(_impactThreshold);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Step"/> does nothing. A frozen world keeps its accumulator.
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
    /// Gets a value indicating whether a body moves and the world is not frozen. A dragged body that stays still for 20 steps does not keep the world awake.
    /// </summary>
    public bool IsAwake => !_frozen && _awake;

    /// <summary>
    /// Gets or sets the linear speed, in meters per second, below which a body counts as still. A body that stays still for 20 steps goes to sleep.
    /// </summary>
    public float StopSpeed { get; set; } = 0.03f;

    /// <summary>
    /// Gets or sets the angular speed, in radians per second, below which a body counts as still.
    /// </summary>
    public float StopSpin { get; set; } = 0.05f;

    /// <summary>
    /// Gets the number of live bodies and characters. Static shapes do not count.
    /// </summary>
    public int Count => _slotCount - _freeCount;

    /// <summary>
    /// Gets the bodies whose pose changed in the last <see cref="Step"/> or <see cref="StepOnce"/>. The span is valid until the next step.
    /// </summary>
    public ReadOnlySpan<PhysicsBody3D> Moved => _moved.AsSpan(0, _movedCount);

    /// <summary>
    /// Gets the impacts of the last <see cref="Step"/> or <see cref="StepOnce"/>. The span is valid until the next step.
    /// </summary>
    public ReadOnlySpan<PhysicsImpact3D> Impacts => _impacts.AsSpan(0, _impactCount);

    /// <summary>
    /// Adds an unrotated static box with <see cref="PhysicsMaterial.Default"/>. It does not wake the world.
    /// </summary>
    /// <param name="center">The box center in world space.</param>
    /// <param name="halfExtents">The half size of the box on each axis.</param>
    /// <returns>The ID that <see cref="RemoveStatic"/> takes.</returns>
    public int AddStaticBox(Vector3 center, Vector3 halfExtents) =>
        _backend.AddStaticBox(center, halfExtents, Quaternion.Identity, PhysicsMaterial.Default);

    /// <summary>
    /// Adds a static box. It does not wake the world.
    /// </summary>
    /// <param name="center">The box center in world space.</param>
    /// <param name="halfExtents">The half size of the box on each axis.</param>
    /// <param name="orientation">The rotation of the box.</param>
    /// <param name="material">The surface values of the box.</param>
    /// <returns>The ID that <see cref="RemoveStatic"/> takes.</returns>
    public int AddStaticBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, in PhysicsMaterial material) =>
        _backend.AddStaticBox(center, halfExtents, orientation, material);

    /// <summary>
    /// Adds a static triangle mesh with <see cref="PhysicsMaterial.Default"/>. It does not wake the world.
    /// </summary>
    /// <param name="vertices">The mesh vertices in world space.</param>
    /// <param name="indices">Three vertex indices for each triangle.</param>
    /// <returns>The ID that <see cref="RemoveStatic"/> takes.</returns>
    public int AddStaticMesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices) =>
        _backend.AddStaticMesh(vertices, indices, PhysicsMaterial.Default);

    /// <summary>
    /// Removes a static shape.
    /// </summary>
    /// <param name="id">The ID that <see cref="AddStaticBox(Vector3, Vector3)"/> or <see cref="AddStaticMesh"/> returned.</param>
    public void RemoveStatic(int id) => _backend.RemoveStatic(id);

    /// <summary>
    /// Adds a dynamic box body.
    /// </summary>
    /// <param name="halfExtents">The half size of the box on each axis, in meters.</param>
    /// <param name="pose">The initial pose of the box center.</param>
    /// <param name="material">The material of the body.</param>
    /// <param name="awake">Whether the body starts awake.</param>
    /// <returns>The handle of the new body.</returns>
    public PhysicsBody3D AddBox(Vector3 halfExtents, in PhysicsPose3D pose, in PhysicsMaterial material, bool awake = true) =>
        Add(PhysicsShape3D.Box, halfExtents, pose, material, awake);

    /// <summary>
    /// Adds a dynamic cylinder body along the y axis of its pose.
    /// </summary>
    /// <param name="radius">The cylinder radius, in meters.</param>
    /// <param name="halfHeight">Half the cylinder length, in meters.</param>
    /// <param name="pose">The initial pose of the cylinder center.</param>
    /// <param name="material">The material of the body.</param>
    /// <param name="awake">Whether the body starts awake.</param>
    /// <returns>The handle of the new body.</returns>
    public PhysicsBody3D AddCylinder(float radius, float halfHeight, in PhysicsPose3D pose, in PhysicsMaterial material, bool awake = true) =>
        Add(PhysicsShape3D.Cylinder, new Vector3(radius, halfHeight, radius), pose, material, awake);

    /// <summary>
    /// Adds an upright capsule character that a character controller drives. It wakes the world.
    /// </summary>
    /// <param name="radius">The capsule radius, in meters.</param>
    /// <param name="height">The full capsule height, in meters.</param>
    /// <param name="feet">The position of the bottom of the capsule.</param>
    /// <param name="mass">The mass, in kilograms.</param>
    /// <param name="jumpSpeed">The upward speed of a jump, in meters per second.</param>
    /// <returns>The new character.</returns>
    public PhysicsCharacter3D AddCharacter(float radius, float height, Vector3 feet, float mass = 70, float jumpSpeed = 4.2f)
    {
        var index = AllocateSlot();
        var center = feet + new Vector3(0, height * 0.5f, 0);
        var pose = PhysicsPose3D.At(center);
        _slots[index] = new Slot
        {
            Live = true,
            Generation = _slots[index].Generation + 1,
            Character = true,
            Previous = pose,
            Current = pose,
        };
        _backend.CreateCharacter(index, radius, height, center, mass, jumpSpeed);
        _awake = true;
        return new PhysicsCharacter3D(this, new PhysicsBody3D(this, index, _slots[index].Generation), radius, height);
    }

    /// <summary>
    /// Removes a body or a character. Every handle to it becomes stale. It does nothing for a handle that is not live in this world.
    /// </summary>
    /// <param name="body">The body to remove.</param>
    public void Remove(PhysicsBody3D body)
    {
        if (!IsLive(body))
        {
            return;
        }

        _backend.DestroyBody(body.Slot);
        _slots[body.Slot].Live = false;
        _free[_freeCount++] = body.Slot;
    }

    /// <summary>
    /// Sets the pose of a body with no sweep and no blend. Unlike <see cref="PhysicsBody.Teleport"/>, it wakes the body and the world.
    /// </summary>
    /// <param name="body">The body to move.</param>
    /// <param name="pose">The new pose.</param>
    /// <exception cref="InvalidOperationException">The body is not live in this world.</exception>
    public void Teleport(PhysicsBody3D body, in PhysicsPose3D pose)
    {
        ref var slot = ref Checked(body);
        _backend.SetPose(body.Slot, pose);
        slot.Previous = pose;
        slot.Current = pose;
        _backend.SetAwake(body.Slot, true);
        _awake = true;
    }

    /// <summary>
    /// Sets the velocity of a body. A nonzero velocity wakes the world.
    /// </summary>
    /// <param name="body">The body to change.</param>
    /// <param name="linear">The linear velocity, in meters per second.</param>
    /// <param name="angular">The angular velocity, in radians per second.</param>
    /// <exception cref="InvalidOperationException">The body is not live in this world.</exception>
    public void SetVelocity(PhysicsBody3D body, Vector3 linear, Vector3 angular)
    {
        _ = Checked(body);
        _backend.SetVelocity(body.Slot, linear, angular);
        _awake |= linear != Vector3.Zero || angular != Vector3.Zero;
    }

    /// <summary>
    /// Starts a drag with soft servos at a grab point. The body stays dynamic, so it collides and swings on the grab point. A character ignores it.
    /// </summary>
    /// <param name="body">The body to drag.</param>
    /// <param name="worldPoint">The grab point in world space.</param>
    /// <exception cref="InvalidOperationException">The body is not live in this world.</exception>
    public void BeginDrag(PhysicsBody3D body, Vector3 worldPoint)
    {
        ref var slot = ref Checked(body);
        if (slot.Character)
        {
            return;
        }

        _backend.ReadPose(body.Slot, out var pose);
        slot.Dragging = true;
        slot.Resting = 0;
        slot.GrabLocal = pose.ToLocal(worldPoint);
        slot.DragTarget = worldPoint;
        slot.DragTurn = default;
        _backend.BeginGrab(body.Slot, slot.GrabLocal, worldPoint);
        _awake = true;
    }

    /// <summary>
    /// Moves the target of the grab point. A target equal to the last one does nothing, so a call on each frame does not wake the world.
    /// </summary>
    /// <param name="body">The dragged body. A body that is not dragged does nothing.</param>
    /// <param name="worldPoint">The new target in world space.</param>
    public void DragTo(PhysicsBody3D body, Vector3 worldPoint)
    {
        if (!IsLive(body) || !_slots[body.Slot].Dragging)
        {
            return;
        }

        ref var slot = ref _slots[body.Slot];
        if (slot.DragTarget == worldPoint)
        {
            return;
        }

        slot.DragTarget = worldPoint;
        slot.Resting = 0;
        _backend.MoveGrab(body.Slot, worldPoint);
        _awake = true;
    }

    /// <summary>
    /// Sets the orientation that the angular servo turns the body toward, about the grab point. An orientation equal to the last one does nothing.
    /// </summary>
    /// <param name="body">The dragged body. A body that is not dragged does nothing.</param>
    /// <param name="orientation">The target orientation. The method normalizes it.</param>
    public void DragTurn(PhysicsBody3D body, Quaternion orientation)
    {
        if (!IsLive(body) || !_slots[body.Slot].Dragging)
        {
            return;
        }

        ref var slot = ref _slots[body.Slot];
        var turn = Quaternion.Normalize(orientation);
        if (slot.DragTurn == turn)
        {
            return;
        }

        slot.DragTurn = turn;
        slot.Resting = 0;
        _backend.TurnGrab(body.Slot, turn);
        _awake = true;
    }

    /// <summary>
    /// Ends a drag, removes the servos and sets the linear velocity. The body keeps its angular velocity.
    /// </summary>
    /// <param name="body">The dragged body. A body that is not dragged does nothing.</param>
    /// <param name="velocity">The release velocity, in meters per second.</param>
    public void EndDrag(PhysicsBody3D body, Vector3 velocity)
    {
        if (!IsLive(body) || !_slots[body.Slot].Dragging)
        {
            return;
        }

        _slots[body.Slot].Dragging = false;
        _slots[body.Slot].Resting = 0;
        _backend.EndGrab(body.Slot);
        _backend.ReadVelocity(body.Slot, out _, out var angular);
        _backend.SetVelocity(body.Slot, velocity, angular);
        _awake = true;
    }

    /// <summary>
    /// Gets a value that tells whether a drag holds a body.
    /// </summary>
    /// <param name="body">The body to test.</param>
    /// <returns><see langword="true"/> when the body is live and dragged.</returns>
    public bool IsDragging(PhysicsBody3D body) => IsLive(body) && _slots[body.Slot].Dragging;

    /// <summary>
    /// Gets the grab point of the last drag in world space, at the pose of the last step.
    /// </summary>
    /// <param name="body">The body to read.</param>
    /// <returns>The grab point in world space.</returns>
    /// <exception cref="InvalidOperationException">The body is not live in this world.</exception>
    public Vector3 GrabPoint(PhysicsBody3D body)
    {
        ref var slot = ref Checked(body);
        return slot.Current.ToWorld(slot.GrabLocal);
    }

    /// <summary>
    /// Advances the world to a frame tick with fixed steps and an accumulator. A tick that does not advance past the last one steps nothing.
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
        if (!_awake)
        {
            _clock.Reset();
            _accumulator = 0;
        }

        return _awake;
    }

    /// <summary>
    /// Runs exactly one fixed step, with no clock and no accumulator. It ignores <see cref="Frozen"/> and the idle rule.
    /// </summary>
    public void StepOnce()
    {
        _movedCount = 0;
        _impactCount = 0;
        SubStep();
        RefreshAwake();
    }

    /// <summary>
    /// Releases the engine state. After it, <see cref="Step"/> does nothing.
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

    internal bool IsLive(PhysicsBody3D body) =>
        ReferenceEquals(body.World, this) && (uint)body.Slot < (uint)_slotCount &&
        _slots[body.Slot].Live && _slots[body.Slot].Generation == body.Generation;

    internal PhysicsPose3D Pose(PhysicsBody3D body)
    {
        ref var slot = ref Checked(body);
        var alpha = Math.Clamp(_accumulator / (float)StepNanos, 0, 1);
        return PhysicsPose3D.Lerp(slot.Previous, slot.Current, alpha);
    }

    internal PhysicsPose3D CurrentPose(PhysicsBody3D body) => Checked(body).Current;

    internal (Vector3 Linear, Vector3 Angular) VelocityOf(PhysicsBody3D body)
    {
        _ = Checked(body);
        _backend.ReadVelocity(body.Slot, out var linear, out var angular);
        return (linear, angular);
    }

    internal bool IsBodyAwake(PhysicsBody3D body)
    {
        _ = Checked(body);
        return _backend.IsAwake(body.Slot);
    }

    internal ulong TagOf(PhysicsBody3D body) => Checked(body).Tag;

    internal void SetTag(PhysicsBody3D body, ulong tag) => Checked(body).Tag = tag;

    internal double MassOf(PhysicsBody3D body)
    {
        _ = Checked(body);
        return _backend.MassOf(body.Slot);
    }

    internal bool IsSupported(PhysicsCharacter3D character) =>
        IsLive(character.Body) && _backend.IsSupported(character.Body.Slot);

    internal void SetCharacterGoal(PhysicsCharacter3D character, Vector2 wish, Vector3 view, bool jump)
    {
        if (!IsLive(character.Body))
        {
            return;
        }

        ref var slot = ref _slots[character.Body.Slot];
        if (slot.Wish == wish && !jump && (wish == Vector2.Zero || slot.View == view))
        {
            return;
        }

        slot.Wish = wish;
        slot.View = view;
        _backend.SetCharacterGoal(character.Body.Slot, wish, view, jump);
        _awake = true;
    }

    internal void RequestJump(PhysicsCharacter3D character)
    {
        if (IsLive(character.Body))
        {
            ref var slot = ref _slots[character.Body.Slot];
            _backend.SetCharacterGoal(character.Body.Slot, slot.Wish, slot.View, jump: true);
            _awake = true;
        }
    }

    private PhysicsBody3D Add(PhysicsShape3D shape, Vector3 halfExtents, in PhysicsPose3D pose, in PhysicsMaterial material, bool awake)
    {
        var index = AllocateSlot();
        _slots[index] = new Slot
        {
            Live = true,
            Generation = _slots[index].Generation + 1,
            Previous = pose,
            Current = pose,
        };
        _backend.CreateBody(index, shape, halfExtents, pose, material, awake);
        _awake |= awake;
        return new PhysicsBody3D(this, index, _slots[index].Generation);
    }

    private ref Slot Checked(PhysicsBody3D body)
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
            AllocationScope.Pause();
            Array.Resize(ref _slots, _slots.Length * 2);
            Array.Resize(ref _free, _free.Length * 2);
            Array.Resize(ref _moved, _moved.Length * 2);
            AllocationScope.Resume();
        }

        return _slotCount++;
    }

    private void SubStep()
    {
        for (var i = 0; i < _slotCount; i++)
        {
            ref var slot = ref _slots[i];
            if (slot.Live)
            {
                slot.Previous = slot.Current;
            }
        }

        _backend.Step(StepSeconds);
        CollectImpacts();
        for (var i = 0; i < _slotCount; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || !_backend.IsAwake(i))
            {
                continue;
            }

            _backend.ReadPose(i, out slot.Current);
            if (slot.Current != slot.Previous && !Contains(i))
            {
                _moved[_movedCount++] = new PhysicsBody3D(this, i, slot.Generation);
            }

            if (slot.Character)
            {
                slot.Resting = 0;
                continue;
            }

            _backend.ReadVelocity(i, out var linear, out var angular);
            if (slot.Dragging)
            {
                slot.Resting = linear.Length() < StopSpeed && angular.Length() < StopSpin
                    ? Math.Min(slot.Resting + 1, RestSteps)
                    : 0;
                continue;
            }

            if (linear.Length() < StopSpeed && angular.Length() < StopSpin)
            {
                if (++slot.Resting >= RestSteps)
                {
                    _backend.SetVelocity(i, Vector3.Zero, Vector3.Zero);
                    _backend.SetAwake(i, false);
                    slot.Resting = 0;
                }
            }
            else
            {
                slot.Resting = 0;
            }
        }
    }

    private bool Contains(int index)
    {
        for (var i = 0; i < _movedCount; i++)
        {
            if (_moved[i].Slot == index)
            {
                return true;
            }
        }

        return false;
    }

    private void CollectImpacts()
    {
        var hits = _backend.Hits;
        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (_impactCount == _impacts.Length)
            {
                AllocationScope.Pause();
                Array.Resize(ref _impacts, _impacts.Length * 2);
                AllocationScope.Resume();
            }

            _impacts[_impactCount++] = new PhysicsImpact3D(BodyAt(hit.SlotA), BodyAt(hit.SlotB), hit.Point, hit.Normal, hit.Speed);
        }
    }

    private PhysicsBody3D BodyAt(int index) =>
        index >= 0 && index < _slotCount && _slots[index].Live ? new PhysicsBody3D(this, index, _slots[index].Generation) : default;

    private void RefreshAwake()
    {
        var awake = false;
        for (var i = 0; i < _slotCount && !awake; i++)
        {
            ref var slot = ref _slots[i];
            awake = slot.Live && (slot.Dragging ? slot.Resting < RestSteps : _backend.IsAwake(i));
        }

        _awake = awake;
    }

    private void WakeAll()
    {
        for (var i = 0; i < _slotCount; i++)
        {
            if (_slots[i].Live)
            {
                _backend.SetAwake(i, true);
            }
        }

        _awake = _slotCount > _freeCount;
    }

    private struct Slot
    {
        public bool Live;
        public int Generation;
        public bool Character;
        public bool Dragging;
        public ulong Tag;
        public PhysicsPose3D Previous;
        public PhysicsPose3D Current;
        public Vector3 GrabLocal;
        public Vector3 DragTarget;
        public Quaternion DragTurn;
        public Vector2 Wish;
        public Vector3 View;
        public int Resting;
    }
}
