using System.Numerics;
using Basin.Scene3D;
using Basin.Scene3D.Gltf;

namespace DungeonWm;

internal sealed class RoomBaker
{
    public const float WallHeight = 4;
    public const float DoorHalfWidth = 0.9f;
    public const float DoorHeight = 2.75f;
    public const float FloorTop = 0.05f;
    public const float PillarHalf = 0.75f;
    public const float PropClearance = 0.1f;

    internal static readonly string[] StaticProps = ["table_medium", "shelves", "chest", "keg", "table_small", "banner_red"];
    internal static readonly string[] DynamicPieces = ["barrel_small", "barrel_large", "box_small", "box_large", "crates_stacked", "chair", "stool"];

    private readonly Kit _kit;

    public RoomBaker(Kit kit, BakeSettings settings)
    {
        _kit = kit;
        Settings = settings;
    }

    public BakeSettings Settings { get; }

    public float Unit => Settings.Unit;

    public RoofKind RoofOf(MapRegion region) => Settings.Roof == RoofKind.Mixed
        ? (StableHash.Of(Settings.Seed, region.Id, 0x7007) % 2 == 0 ? RoofKind.Stone : RoofKind.Wood)
        : Settings.Roof;

    public BakedRegion Bake(DungeonWmMap map, MapRegion region)
    {
        var builder = new WorldMeshBuilder();
        var lights = new List<WorldLight>();
        var blockers = new List<WorldBounds>();
        var dynamic = new List<PropPlacement>();
        var scale = Matrix4x4.CreateScale(Unit);
        var roof = RoofOf(region);
        var roofPiece = roof == RoofKind.Wood ? "floor_wood_large" : "floor_tile_large";

        var interior = WorldBounds.Empty;
        foreach (var cell in region.Cells)
        {
            var center = GridSides.CellCenter(cell);
            builder.Add(_kit["floor_tile_large"].Mesh, Matrix4x4.CreateTranslation(center.X, -FloorTop, center.Z) * scale);
            if (roof != RoofKind.None)
            {
                builder.Add(_kit[roofPiece].Mesh,
                    Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(center.X, WallHeight + 0.05f, center.Z) * scale);
            }

            interior = interior
                .Include(new Vector3(4 * cell.X, 0, 4 * cell.Z) * Unit)
                .Include(new Vector3((4 * cell.X) + 4, WallHeight, (4 * cell.Z) + 4) * Unit);
        }

        blockers.Add(new WorldBounds(interior.Min with { Y = -0.2f * Unit }, interior.Max with { Y = 0 }));
        if (roof != RoofKind.None)
        {
            blockers.Add(new WorldBounds(interior.Min with { Y = WallHeight * Unit }, interior.Max with { Y = (WallHeight + 0.3f) * Unit }));
        }

        if (roof == RoofKind.Wood && Settings.RoofBeams && region.Kind == RegionKind.Room)
        {
            var beam = _kit["pillar"].Mesh;
            for (var z = region.Z0; z < region.Z0 + region.Depth; z += 2)
            {
                for (var x = region.X0; x < region.X0 + region.Width; x++)
                {
                    var transform = Matrix4x4.CreateScale(1, 1, 0.45f) * Matrix4x4.CreateScale(0.45f, 1, 1) *
                        Matrix4x4.CreateRotationZ(-MathF.PI / 2) *
                        Matrix4x4.CreateTranslation(4 * x, WallHeight - 0.4f, (4 * z) + 2) * scale;
                    builder.Add(beam, transform);
                    blockers.Add(beam.Bounds.Transformed(transform));
                }
            }
        }

        foreach (var wall in map.WallsOf(region))
        {
            if (wall.Kind == WallKind.Shared)
            {
                continue;
            }

            var center = GridSides.WallCenter(wall.Edge.Cell, wall.Edge.Side);
            var yaw = GridSides.InwardYaw(wall.Edge.Side);
            var placement = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(center);
            builder.Add(_kit[wall.Piece].Mesh, placement * scale);
            if (wall.Kind == WallKind.Doorway)
            {
                blockers.Add(Box(-2, -DoorHalfWidth, 0, WallHeight, placement));
                blockers.Add(Box(DoorHalfWidth, 2, 0, WallHeight, placement));
                blockers.Add(Box(-DoorHalfWidth, DoorHalfWidth, DoorHeight, WallHeight, placement));
            }
            else
            {
                blockers.Add(Box(-2, 2, 0, WallHeight, placement));
            }

            if (wall.Torch)
            {
                var mount = placement * Matrix4x4.CreateTranslation(0, Settings.TorchHeight / Unit, 0);
                var torch = Matrix4x4.CreateTranslation(0, 0, 0.5f) * mount;
                builder.Add(_kit["torch_mounted"].Mesh, torch * scale);
                var flame = Vector3.Transform(new Vector3(0, 0.62f, 0.42f), torch * scale);
                var phase = StableHash.Of(Settings.Seed, wall.Edge.Cell.X, wall.Edge.Cell.Z, (int)wall.Edge.Side) % 628 / 100f;
                lights.Add(new WorldLight(flame, Settings.Torch, 11, 1.6f, Settings.TorchFlicker, phase));
            }
        }

        if (region.Kind == RegionKind.Room)
        {
            AddCorners(region, builder, blockers, scale);
            AddProps(map, region, builder, blockers, dynamic, scale);
        }

        return new BakedRegion(builder.Build(_kit.Images[0]), lights, blockers, dynamic, interior, roof);

        WorldBounds Box(float x0, float x1, float y0, float y1, in Matrix4x4 placement) =>
            new WorldBounds(new Vector3(x0, y0, -0.5f), new Vector3(x1, y1, 0.5f)).Transformed(placement * scale);
    }

    private void AddCorners(MapRegion region, WorldMeshBuilder builder, List<WorldBounds> blockers, in Matrix4x4 scale)
    {
        var pillar = _kit["pillar"].Mesh;
        var corners = new[]
        {
            new Vector3(4 * region.X0, 0, 4 * region.Z0),
            new Vector3(4 * (region.X0 + region.Width), 0, 4 * region.Z0),
            new Vector3(4 * region.X0, 0, 4 * (region.Z0 + region.Depth)),
            new Vector3(4 * (region.X0 + region.Width), 0, 4 * (region.Z0 + region.Depth)),
        };
        foreach (var corner in corners)
        {
            var transform = Matrix4x4.CreateTranslation(corner) * scale;
            builder.Add(pillar, transform);
            blockers.Add(pillar.Bounds.Transformed(transform));
        }
    }

    private void AddProps(
        DungeonWmMap map, MapRegion region, WorldMeshBuilder builder, List<WorldBounds> blockers, List<PropPlacement> dynamic, in Matrix4x4 scale)
    {
        var corners = new (int X, int Z, float Yaw)[]
        {
            (region.X0, region.Z0, MathF.PI * 0.25f),
            (region.X0 + region.Width - 1, region.Z0, -MathF.PI * 0.25f),
            (region.X0, region.Z0 + region.Depth - 1, MathF.PI * 0.75f),
            (region.X0 + region.Width - 1, region.Z0 + region.Depth - 1, -MathF.PI * 0.75f),
        };
        for (var i = 0; i < corners.Length; i++)
        {
            var (x, z, yaw) = corners[i];
            var cell = new GridCell(x, z);
            if (NearDoor(map, region, cell))
            {
                continue;
            }

            var roll = StableHash.Of(Settings.Seed, region.Id, i, 0x9e37) % 1000 / 1000.0;
            if (roll >= Settings.Props)
            {
                continue;
            }

            var pick = StableHash.Of(Settings.Seed, region.Id, i, 0xabc);
            if (Settings.DynamicProps && pick % 2 == 0)
            {
                var moving = DynamicPieces[pick / 2 % DynamicPieces.Length];
                dynamic.Add(new PropPlacement(moving, CornerPosition(region, x, z, yaw, moving) * Unit, yaw));
                continue;
            }

            var piece = StaticProps[pick / 2 % StaticProps.Length];
            var position = CornerPosition(region, x, z, yaw, piece);
            var transform = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position) * scale;
            builder.Add(_kit[piece].Mesh, transform);
            if (KitManifest.Find(piece).Shape != KitShape.None)
            {
                blockers.Add(_kit[piece].Bounds.Transformed(transform));
            }
        }
    }

    internal Vector3 CornerPosition(MapRegion region, int x, int z, float yaw, string piece)
    {
        var bounds = _kit[piece].Bounds.Transformed(Matrix4x4.CreateRotationY(yaw));
        var clear = PillarHalf + PropClearance;
        var west = x == region.X0;
        var north = z == region.Z0;
        var px = west
            ? MathF.Max((4 * x) + 1.4f, (4 * region.X0) + clear - bounds.Min.X)
            : MathF.Min((4 * x) + 2.6f, (4 * (region.X0 + region.Width)) - clear - bounds.Max.X);
        var pz = north
            ? MathF.Max((4 * z) + 1.4f, (4 * region.Z0) + clear - bounds.Min.Z)
            : MathF.Min((4 * z) + 2.6f, (4 * (region.Z0 + region.Depth)) - clear - bounds.Max.Z);
        return new Vector3(px, 0, pz);
    }

    private static bool NearDoor(DungeonWmMap map, MapRegion region, GridCell cell)
    {
        foreach (var doorway in map.Doorways)
        {
            if (!ReferenceEquals(doorway.Room, region))
            {
                continue;
            }

            var door = doorway.Edge.Cell.X == cell.X && doorway.Edge.Cell.Z == cell.Z ||
                doorway.Edge.Mirror.Cell.X == cell.X && doorway.Edge.Mirror.Cell.Z == cell.Z;
            if (door)
            {
                return true;
            }
        }

        return false;
    }
}
