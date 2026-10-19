using System.Globalization;
using System.Numerics;
using Basin;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Kinematics;
using Basin.Physics;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private const float RehangSeconds = 0.35f;
    private const float FlashSeconds = 0.4f;
    private const float RollPerAxisUnit = 0.01f;
    private static readonly float RehangFacing = MathF.Cos(20 * MathF.PI / 180);

    private readonly List<Rehang> _rehangs = [];
    private VelocityTracker3 _pullVelocity;
    private DungeonWmPhysics? _physics;
    private Pull? _pull;

    private sealed class Pull
    {
        public required DungeonWmWindow Window { get; init; }

        public required Vector3 GrabPoint { get; init; }

        public required Vector3 Normal { get; init; }

        public required float Depth { get; set; }

        public PhysicsBody3D Body { get; set; }

        public bool Detached { get; set; }

        public Quaternion Held { get; set; } = Quaternion.Identity;
    }

    private sealed class Rehang
    {
        public required DungeonWmWindow Window { get; init; }

        public required MapRegion Room { get; init; }

        public required WallSlot Slot { get; init; }

        public required PhysicsPose3D From { get; init; }

        public float Elapsed { get; set; }
    }

    private void StartPhysics()
    {
        var feet = _camera.Position - new Vector3(0, _settings.EyeHeight, 0);
        _physics = new DungeonWmPhysics(_world, _settings, feet);
        _walker.Character = _physics.Player;
        _world.Rebuilt += () =>
        {
            _physics.SyncStatics();
            _physics.SyncProps();
        };
    }

    private partial bool OnSuperPress(double x, double y) => BeginPull(PickAt(x, y));

    private bool BeginPull(in WorldHit pick)
    {
        if (_physics is null)
        {
            return false;
        }

        if (pick.Window?.Tag is not DungeonWmWindow window || pick.Kind == WorldHitKind.None || window.State == DungeonWmWindowState.Takeover)
        {
            return false;
        }

        _pull = new Pull
        {
            Window = window,
            GrabPoint = pick.Point,
            Normal = window.World.Normal,
            Depth = Vector3.Distance(_camera.Position, pick.Point),
        };
        _pullVelocity.Reset();
        if (window.State == DungeonWmWindowState.Body && _physics.Bodies.TryGetValue(window, out var body))
        {
            _pull.Body = body;
            _pull.Detached = true;
            _pull.Held = HeldFrom(window);
            _physics.World.BeginDrag(body, pick.Point);
            OnPickUp(window);
        }

        if (!_crosshair)
        {
            Focus(window);
        }

        BasinReport.Line($"PULL begin {window.AppId}");
        return true;
    }

    private bool UpdatePull()
    {
        if (_pull is not { } pull || _physics is not { } physics)
        {
            return false;
        }

        var target = PullTarget(pull);
        if (!pull.Detached)
        {
            var pulled = Vector3.Dot(target - pull.GrabPoint, pull.Normal);
            if (pulled < _settings.PullDistance)
            {
                return true;
            }

            Detach(pull.Window);
            pull.Body = physics.Bodies[pull.Window];
            pull.Detached = true;
            pull.Held = HeldFrom(pull.Window);
            physics.World.BeginDrag(pull.Body, pull.GrabPoint);
            OnPickUp(pull.Window);
        }

        physics.World.DragTo(pull.Body, target);
        physics.World.DragTurn(pull.Body, Heading() * pull.Held);
        Sample(target);
        if (physics.World.IsAwake)
        {
            ScheduleAll();
        }

        return true;
    }

    private static string Facing(DungeonWmWindow window)
    {
        var normal = window.World.Normal;
        var up = Vector3.Transform(Vector3.UnitY, window.World.Orientation);
        return string.Create(CultureInfo.InvariantCulture, $"{normal.X:F2},{normal.Y:F2},{normal.Z:F2} up={up.X:F2},{up.Y:F2},{up.Z:F2}");
    }

    private static float YawOf(DungeonWmWindow window)
    {
        var normal = window.World.Normal;
        return MathF.Atan2(normal.X, normal.Z);
    }

    private Quaternion Heading() => Quaternion.CreateFromAxisAngle(Vector3.UnitY, _camera.Yaw);

    private Quaternion HeldFrom(DungeonWmWindow window) =>
        Quaternion.Normalize(Quaternion.Inverse(Heading()) * window.World.Orientation);

    private bool TurnHeld(float yaw, float pitch, float roll)
    {
        if (_pull is not { Detached: true } pull)
        {
            return false;
        }

        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll);
        pull.Held = Quaternion.Normalize(turn * pull.Held);
        UpdatePull();
        return true;
    }

    private bool SquareHeld()
    {
        if (_pull is not { Detached: true } pull)
        {
            return false;
        }

        var facing = Vector3.Transform(Vector3.UnitZ, Heading() * pull.Held);
        var yaw = MathF.Round(MathF.Atan2(facing.X, facing.Z) / (MathF.PI / 2)) * (MathF.PI / 2);
        pull.Held = Quaternion.Normalize(Quaternion.Inverse(Heading()) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
        BasinReport.Line(string.Create(CultureInfo.InvariantCulture, $"SQUARE {pull.Window.AppId} yaw={yaw * 180 / MathF.PI:F0}"));
        UpdatePull();
        return true;
    }

    private void EndPull()
    {
        if (_pull is not { } pull || _physics is not { } physics)
        {
            _pull = null;
            return;
        }

        _pull = null;
        if (!pull.Detached)
        {
            BasinReport.Line($"PULL cancel {pull.Window.AppId}");
            return;
        }

        var velocity = ThrowVelocity() * _settings.ThrowScale;
        physics.World.EndDrag(pull.Body, velocity);
        var speed = velocity.Length();
        BasinReport.Line(string.Create(CultureInfo.InvariantCulture, $"THROW {pull.Window.AppId} speed={speed:F2}"));
        if (speed > _settings.ThrowMinSpeed)
        {
            OnThrow(pull.Window);
        }
        else
        {
            physics.WriteWindows();
            TryRehang(pull.Window);
        }

        ScheduleAll();
    }

    private Vector3 PullTarget(Pull pull)
    {
        return _camera.TryRayAt(_layout, _pointer.X, _pointer.Y, out var ray) ? ray.At(pull.Depth) : pull.GrabPoint;
    }

    private bool PullAxis(double value)
    {
        if (_pull is not { } pull)
        {
            return false;
        }

        if (Rotating)
        {
            TurnHeld(0, 0, (float)value * RollPerAxisUnit);
            return true;
        }

        pull.Depth = Math.Clamp(pull.Depth - ((float)value * 0.01f), 0.3f, 20);
        UpdatePull();
        return true;
    }

    private void Sample(Vector3 point) => _pullVelocity.Add(point, MonotonicClock.Nanos);

    private Vector3 ThrowVelocity() => _pullVelocity.Velocity(MonotonicClock.Nanos);

    private void Detach(DungeonWmWindow window)
    {
        if (_physics is null || window.State == DungeonWmWindowState.Body)
        {
            return;
        }

        _rehangs.RemoveAll(r => ReferenceEquals(r.Window, window));
        _world.Release(window);
        window.Slot = null;
        window.State = DungeonWmWindowState.Body;
        _physics.Detach(window);
        BasinReport.Line($"DETACHED {window.AppId}");
        RaiseEvent("dungeonwm/window-detached");
        ScheduleAll();
    }

    private partial void UnbindBody(DungeonWmWindow window)
    {
        _rehangs.RemoveAll(r => ReferenceEquals(r.Window, window));
        if (_pull?.Window == window)
        {
            _pull = null;
        }

        _physics?.Release(window);
        if (window.State == DungeonWmWindowState.Body)
        {
            window.State = DungeonWmWindowState.Pending;
        }
    }

    private partial bool StepWorld(float seconds, long now)
    {
        if (_physics is not { } physics)
        {
            return false;
        }

        physics.World.Frozen = _locked || _inspectorFreeze;
        if (_pull is not null)
        {
            UpdatePull();
        }

        var awake = physics.World.Step(new FrameTick(now, PhysicsWorld3D.StepNanos));
        RecordStep(physics.World.Moved.Length, physics.World.Impacts.Length);
        physics.WriteProps();
        physics.WriteWindows();
        _walker.Sync();
        UpdateAvatar(seconds);
        if (_walker.JustJumped)
        {
            OnJump();
        }

        if (_walker.JustLanded)
        {
            OnLand();
        }

        foreach (var impact in physics.World.Impacts)
        {
            FlashBody(impact.A);
            FlashBody(impact.B);
            SoundImpact(impact);
        }

        var flashing = false;
        foreach (var window in _windows)
        {
            if (window.World.Flash > 0)
            {
                window.World.Flash = MathF.Max(0, window.World.Flash - (seconds / FlashSeconds));
                flashing = true;
            }
        }

        CheckRehang();
        var rehanging = StepRehangs(seconds);
        return awake || flashing || rehanging;
    }

    private partial bool WorldBusy() =>
        (_physics?.World.IsAwake ?? false) || _rehangs.Count > 0 || (_avatar is { Animating: true } && AvatarSeen());

    private void FlashBody(PhysicsBody3D body)
    {
        if (_physics?.WindowOf(body) is { } window)
        {
            window.World.Flash = 1;
        }
    }

    private void CheckRehang()
    {
        if (_physics is not { } physics)
        {
            return;
        }

        foreach (var (window, body) in physics.Bodies)
        {
            if (_pull?.Window == window || physics.World.IsDragging(body) || body.Velocity.Length() > 0.05f)
            {
                continue;
            }

            if (TryRehang(window))
            {
                break;
            }
        }
    }

    private bool TryRehang(DungeonWmWindow window)
    {
        if (_physics is not { } physics || window.Room is not { } room)
        {
            return false;
        }

        var center = window.World.Position;
        var normal = window.World.Normal;
        WallSlot? best = null;
        var bestDistance = _settings.RehangDistance;
        foreach (var slot in _world.SlotsOf(room))
        {
            if (!_world.IsFree(slot) || Vector3.Dot(normal, slot.Inward) < RehangFacing)
            {
                continue;
            }

            var hang = HangPosition(window, slot, wide: false);
            var offset = center - hang;
            var off = Vector3.Dot(offset, slot.Inward);
            var along = new Vector3(offset.X, 0, offset.Z) - (slot.Inward * off);
            var distance = MathF.Abs(off) + (along.Length() * 0.25f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = slot;
            }
        }

        if (best is not { } chosen)
        {
            return false;
        }

        _rehangs.Add(new Rehang
        {
            Window = window,
            Room = room,
            Slot = chosen,
            From = new PhysicsPose3D(window.World.Position, window.World.Orientation),
        });
        _world.Occupy(chosen, window);
        window.State = DungeonWmWindowState.Pending;
        physics.Release(window);
        BasinReport.Line($"REHANG {window.AppId} slot={SlotName(chosen)}");
        return true;
    }

    private bool StepRehangs(float seconds)
    {
        for (var i = _rehangs.Count - 1; i >= 0; i--)
        {
            var rehang = _rehangs[i];
            rehang.Elapsed += seconds;
            var t = Math.Clamp(rehang.Elapsed / RehangSeconds, 0, 1);
            var ease = 1 - ((1 - t) * (1 - t) * (1 - t));
            var slot = rehang.Slot;
            var target = HangPosition(rehang.Window, slot, wide: false);
            var orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, slot.Yaw);
            rehang.Window.World.Position = Vector3.Lerp(rehang.From.Position, target, ease);
            rehang.Window.World.Orientation = Quaternion.Slerp(rehang.From.Orientation, orientation, ease);
            if (t >= 1)
            {
                _rehangs.RemoveAt(i);
                _world.Release(rehang.Window);
                Hang(rehang.Window, rehang.Room, slot);
                BasinReport.Line($"HUNG {rehang.Window.AppId} room={rehang.Room.Workspace + 1} slot={SlotName(rehang.Window.Slot)}");
                SoundHung(rehang.Window);
                RaiseEvent("dungeonwm/window-hung");
            }
        }

        return _rehangs.Count > 0;
    }

    private partial void RegisterPhysicsCommands(IpcMethodRegistry methods)
    {
        _report.Register(methods, "dungeonwm/throw", "throw {id} {vx:number} {vy:number} {vz:number}",
            Describe("Pull a window off its wall and throw it with a velocity in meters per second.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply _) =>
            {
                var id = p.GetString("id");
                var vx = p.GetDouble("vx");
                var vy = p.GetDouble("vy");
                var vz = p.GetDouble("vz");
                if (p.Failed)
                {
                    return;
                }

                if (FindWindow(id) is not { } window || _physics is not { } physics)
                {
                    p.Invalid($"no window {id} or no physics");
                    return;
                }

                Detach(window);
                if (physics.Bodies.TryGetValue(window, out var body))
                {
                    var velocity = new Vector3((float)vx, (float)vy, (float)vz);
                    physics.World.SetVelocity(body, velocity, new Vector3(0.6f, 0.3f, 0));
                    BasinReport.Line(string.Create(CultureInfo.InvariantCulture, $"THROW {window.AppId} speed={velocity.Length():F2}"));
                    if (velocity.Length() > _settings.ThrowMinSpeed)
                    {
                        OnThrow(window);
                    }
                }

                ScheduleAll();
            });

        _report.Register(methods, "dungeonwm/turn", "turn {yaw:number} {pitch:number} {roll:number}",
            Describe("Turn the held window by yaw about up, pitch about the view's right and roll about the view, in degrees.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply _) =>
            {
                var yaw = p.GetDouble("yaw");
                var pitch = p.GetDouble("pitch");
                var roll = p.GetDouble("roll");
                if (p.Failed)
                {
                    return;
                }

                if (!TurnHeld((float)(yaw * Math.PI / 180), (float)(pitch * Math.PI / 180), (float)(roll * Math.PI / 180)))
                {
                    p.Invalid("no window is held");
                }
            });

        _report.Register(methods, "dungeonwm/square", "square",
            Describe("Stand the held window upright, facing the nearest quarter turn.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply _) =>
            {
                if (!SquareHeld())
                {
                    p.Invalid("no window is held");
                }
            });

        _report.Register(methods, "dungeonwm/physics-state", "physics-state",
            Describe("Print the physics world: awake, the bodies, the props and the player.", IpcMethodTraits.ReadOnly),
            PrintPhysics);
    }

    private partial void PrintPhysics()
    {
        if (_physics is not { } physics)
        {
            _report.Line("PHYSICS off");
            return;
        }

        var player = physics.Player.Position;
        _report.Line(string.Create(CultureInfo.InvariantCulture,
            $"PHYSICS awake={(physics.World.IsAwake ? "yes" : "no")} frozen={(physics.World.Frozen ? "yes" : "no")} bodies={physics.Bodies.Count} props={physics.PropBodies} player={player.X:F2},{player.Y:F2},{player.Z:F2} supported={(physics.Player.Supported ? "yes" : "no")} rehanging={_rehangs.Count}"));
        foreach (var (window, body) in physics.Bodies)
        {
            var pose = body.Pose;
            _report.Line(string.Create(CultureInfo.InvariantCulture,
                $"BODY {window.AppId} at={pose.Position.X:F2},{pose.Position.Y:F2},{pose.Position.Z:F2} yaw={YawOf(window) * 180 / MathF.PI:F1} facing={Facing(window)} speed={body.Velocity.Length():F2} awake={(body.IsAwake ? "yes" : "no")}"));
        }
    }

    private partial void OnPickUp(DungeonWmWindow window);

    private partial void OnThrow(DungeonWmWindow window);

    private partial void OnLand();
}
