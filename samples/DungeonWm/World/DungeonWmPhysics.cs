using System.Numerics;
using Basin;
using Basin.Physics;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed class DungeonWmPhysics : IDisposable
{
    public const float PlayerRadius = 0.35f;
    public const float PlayerHeight = 1.7f;

    private readonly DungeonWmWorld _world;
    private readonly DungeonWmSettings _settings;
    private readonly Dictionary<MapRegion, List<int>> _statics = [];
    private readonly Dictionary<DungeonWmProp, PhysicsBody3D> _props = [];
    private readonly Dictionary<DungeonWmWindow, PhysicsBody3D> _windows = [];
    private readonly Dictionary<DungeonWmSnapshot, PhysicsBody3D> _snapshots = [];

    public DungeonWmPhysics(DungeonWmWorld world, DungeonWmSettings settings, Vector3 feet)
    {
        _world = world;
        _settings = settings;
        World = new PhysicsWorld3D(Bepu3DEngine.Instance, new Vector3(0, -settings.Gravity, 0)) { ImpactThreshold = 1.2 };
        SyncStatics();
        SyncProps();
        Player = World.AddCharacter(PlayerRadius, PlayerHeight, feet, jumpSpeed: 4.2f);
    }

    public PhysicsWorld3D World { get; }

    public PhysicsCharacter3D Player { get; }

    public IReadOnlyDictionary<DungeonWmWindow, PhysicsBody3D> Bodies => _windows;

    public IReadOnlyDictionary<DungeonWmSnapshot, PhysicsBody3D> SnapshotBodies => _snapshots;

    public int PropBodies => _props.Count;

    public IReadOnlyDictionary<DungeonWmProp, PhysicsBody3D> Props => _props;

    public void SyncStatics()
    {
        foreach (var region in _world.Map.Regions)
        {
            if (_statics.Remove(region, out var ids))
            {
                foreach (var id in ids)
                {
                    World.RemoveStatic(id);
                }
            }

            var room = _world.RoomOf(region);
            ids = new List<int>(room.Blockers.Count);
            foreach (var box in room.Blockers)
            {
                ids.Add(World.AddStaticBox(box.Center, box.Size * 0.5f, Quaternion.Identity, new PhysicsMaterial(Friction: 0.6, Restitution: 0.2)));
            }

            _statics[region] = ids;
        }
    }

    public void SyncProps()
    {
        foreach (var prop in _world.Props)
        {
            if (_props.ContainsKey(prop) || !_world.Settings.Bake.DynamicProps)
            {
                continue;
            }

            var bounds = prop.Prop.Mesh.Bounds;
            var size = bounds.Size * _world.Unit;
            var local = bounds.Center * _world.Unit;
            var material = new PhysicsMaterial(Density: _settings.PropDensity, Friction: 0.6, Restitution: 0.15, GravityScale: 1, LinearDamping: 0.2);
            var pose = new PhysicsPose3D(prop.Prop.Position + Vector3.Transform(local, prop.Prop.Orientation), prop.Prop.Orientation);
            var body = KitManifest.Find(prop.Piece).Shape == KitShape.Cylinder
                ? World.AddCylinder(MathF.Max(size.X, size.Z) * 0.5f, size.Y * 0.5f, pose, material, awake: false)
                : World.AddBox(size * 0.5f, pose, material, awake: false);
            _props[prop] = body;
        }
    }

    public void WriteProps()
    {
        foreach (var (prop, body) in _props)
        {
            var pose = body.Pose;
            var local = prop.Prop.Mesh.Bounds.Center * _world.Unit;
            prop.Prop.Orientation = pose.Orientation;
            prop.Prop.Position = pose.Position - Vector3.Transform(local, pose.Orientation);
            if (_world.RoomAt(pose.Position) is { } region)
            {
                prop.Prop.Room = _world.RoomOf(region);
            }
        }
    }

    public PhysicsBody3D Detach(DungeonWmWindow window)
    {
        if (_windows.TryGetValue(window, out var existing))
        {
            return existing;
        }

        var body = AddSlab(window.World);
        _windows[window] = body;
        return body;
    }

    public PhysicsBody3D Detach(DungeonWmSnapshot snapshot)
    {
        if (_snapshots.TryGetValue(snapshot, out var existing))
        {
            return existing;
        }

        var body = AddSlab(snapshot.World);
        _snapshots[snapshot] = body;
        return body;
    }

    public bool Adopt(DungeonWmWindow window, DungeonWmSnapshot snapshot)
    {
        if (!_windows.Remove(window, out var body))
        {
            return false;
        }

        _snapshots[snapshot] = body;
        return true;
    }

    public void Release(DungeonWmSnapshot snapshot)
    {
        if (_snapshots.Remove(snapshot, out var body))
        {
            World.Remove(body);
        }
    }

    private PhysicsBody3D AddSlab(WorldWindow slab)
    {
        var half = HalfExtents(slab);
        var center = slab.Position - (slab.Normal * half.Z);
        var density = 40 / MathF.Max(8 * half.X * half.Y * half.Z, 1e-4f) * MathF.Max(slab.Width * slab.Height, 0.05f);
        return World.AddBox(half, new PhysicsPose3D(center, slab.Orientation), new PhysicsMaterial(Density: density, Friction: 0.5, Restitution: 0.25, GravityScale: 1, LinearDamping: 0.15));
    }

    public void Release(DungeonWmWindow window)
    {
        if (_windows.Remove(window, out var body))
        {
            World.Remove(body);
        }
    }

    public void WriteWindows()
    {
        foreach (var (window, body) in _windows)
        {
            window.Room = Write(window.World, body, window.Room);
        }

        foreach (var (snapshot, body) in _snapshots)
        {
            snapshot.Room = Write(snapshot.World, body, snapshot.Room);
        }
    }

    private MapRegion? Write(WorldWindow slab, PhysicsBody3D body, MapRegion? room)
    {
        var pose = body.Pose;
        slab.Orientation = pose.Orientation;
        slab.Position = pose.Position + (Vector3.Transform(Vector3.UnitZ, pose.Orientation) * HalfExtents(slab).Z);
        if (_world.RoomAt(pose.Position) is not { } region || ReferenceEquals(region, room))
        {
            return room;
        }

        slab.Room = _world.RoomOf(region);
        return region;
    }

    public static Vector3 HalfExtents(WorldWindow slab)
    {
        if (slab.ShownFrame is { } frame)
        {
            var outer = frame.OuterSize(slab.Width, slab.Height);
            return new Vector3(outer.X * 0.5f, outer.Y * 0.5f, MathF.Max(frame.Depth, 0.01f) * 0.5f);
        }

        return new Vector3(slab.Width * 0.5f, slab.Height * 0.5f, MathF.Max(slab.Depth, 0.01f) * 0.5f);
    }

    public DungeonWmWindow? WindowOf(PhysicsBody3D body)
    {
        foreach (var (window, candidate) in _windows)
        {
            if (candidate == body)
            {
                return window;
            }
        }

        return null;
    }

    public DungeonWmSnapshot? SnapshotOf(PhysicsBody3D body)
    {
        foreach (var (snapshot, candidate) in _snapshots)
        {
            if (candidate == body)
            {
                return snapshot;
            }
        }

        return null;
    }

    public void Dispose() => World.Dispose();
}
