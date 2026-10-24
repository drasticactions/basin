namespace DungeonWm;

internal sealed class DungeonWmMap
{
    private static readonly string[] DecorPieces =
        ["wall_arched", "wall_cracked", "wall_window_open", "wall_window_closed", "wall_shelves"];

    private readonly Dictionary<GridCell, MapRegion> _cells = [];
    private readonly HashSet<MapEdge> _doorEdges = [];
    private readonly Dictionary<MapRegion, List<MapWall>> _walls = [];
    private int _nextId;

    public DungeonWmMap(MapSettings settings)
    {
        Settings = settings;
    }

    public MapSettings Settings { get; }

    public List<MapRegion> Regions { get; } = [];

    public List<MapRegion> Rooms { get; } = [];

    public List<MapDoorway> Doorways { get; } = [];

    public int Version { get; private set; }

    public MapRegion? RegionAt(GridCell cell) => _cells.GetValueOrDefault(cell);

    public IReadOnlyList<MapWall> WallsOf(MapRegion region)
    {
        if (!_walls.TryGetValue(region, out var walls))
        {
            walls = BuildWalls(region);
            _walls[region] = walls;
        }

        return walls;
    }

    public MapRegion AddRoom()
    {
        var index = Rooms.Count;
        var random = new Random(StableHash.Of(Settings.Seed, index, 0x51ab));
        if (index == 0)
        {
            var first = NewRegion(RegionKind.Room);
            first.Width = random.Next(Settings.RoomMinWidth, Settings.RoomMaxWidth + 1);
            first.Depth = random.Next(Settings.RoomMinDepth, Settings.RoomMaxDepth + 1);
            first.Entrance = GridSide.South;
            Commit(first, null, null);
            return first;
        }

        for (var back = index - 1; back >= 0; back--)
        {
            var previous = Rooms[back];
            if (previous.Removed && back > 0)
            {
                continue;
            }

            foreach (var (side, offset) in Candidates(previous, random))
            {
                if (TryPlace(previous, side, offset, random, out var room, out var corridor))
                {
                    Commit(room, corridor, previous);
                    return room;
                }
            }
        }

        throw new InvalidOperationException("No free wall can take another room.");
    }

    public bool Grow(MapRegion room)
    {
        var sides = new[] { GridSides.Opposite(room.Entrance), GridSides.Left(room.Entrance), GridSides.Right(room.Entrance) };
        foreach (var side in sides)
        {
            var (x0, z0, width, depth) = (room.X0, room.Z0, room.Width, room.Depth);
            switch (side)
            {
                case GridSide.North:
                    z0--;
                    depth++;
                    break;
                case GridSide.South:
                    depth++;
                    break;
                case GridSide.West:
                    x0--;
                    width++;
                    break;
                default:
                    width++;
                    break;
            }

            if (!Free(x0, z0, width, depth, room, margin: 1))
            {
                continue;
            }

            foreach (var cell in room.Cells)
            {
                _cells.Remove(cell);
            }

            (room.X0, room.Z0, room.Width, room.Depth) = (x0, z0, width, depth);
            room.FillRectangle();
            foreach (var cell in room.Cells)
            {
                _cells[cell] = room;
            }

            Invalidate();
            return true;
        }

        return false;
    }

    private void Invalidate()
    {
        _walls.Clear();
        Version++;
    }

    private MapRegion NewRegion(RegionKind kind)
    {
        var region = new MapRegion(_nextId++, kind) { Seed = StableHash.Of(Settings.Seed, _nextId) };
        return region;
    }

    private IEnumerable<(GridSide Side, int Offset)> Candidates(MapRegion room, Random random)
    {
        var list = new List<(GridSide, int)>();
        foreach (var side in GridSides.All)
        {
            if (side == room.Entrance)
            {
                continue;
            }

            var length = side is GridSide.North or GridSide.South ? room.Width : room.Depth;
            for (var offset = 0; offset < length; offset++)
            {
                list.Add((side, offset));
            }
        }

        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private bool TryPlace(MapRegion from, GridSide side, int offset, Random random, out MapRegion room, out MapRegion corridor)
    {
        room = null!;
        corridor = null!;
        var start = side switch
        {
            GridSide.North => new GridCell(from.X0 + offset, from.Z0),
            GridSide.South => new GridCell(from.X0 + offset, from.Z0 + from.Depth - 1),
            GridSide.West => new GridCell(from.X0, from.Z0 + offset),
            _ => new GridCell(from.X0 + from.Width - 1, from.Z0 + offset),
        };

        var path = new List<GridCell>();
        var cell = start;
        var heading = side;
        var length = random.Next(2, 4);
        var bend = random.NextDouble() < 0.5;
        var bendAt = bend ? random.Next(1, length) : -1;
        var turn = random.NextDouble() < 0.5 ? GridSides.Left(side) : GridSides.Right(side);
        var bendLength = random.Next(1, 3);
        for (var i = 0; i < length; i++)
        {
            if (i == bendAt)
            {
                for (var j = 0; j < bendLength; j++)
                {
                    cell = cell.Step(turn);
                    path.Add(cell);
                }
            }

            cell = cell.Step(heading);
            path.Add(cell);
        }

        var width = random.Next(Settings.RoomMinWidth, Settings.RoomMaxWidth + 1);
        var depth = random.Next(Settings.RoomMinDepth, Settings.RoomMaxDepth + 1);
        var along = side is GridSide.North or GridSide.South ? width : depth;
        var entry = random.Next(0, along);
        var last = path[^1];
        var (x0, z0) = side switch
        {
            GridSide.North => (last.X - entry, last.Z - depth),
            GridSide.South => (last.X - entry, last.Z + 1),
            GridSide.West => (last.X - width, last.Z - entry),
            _ => (last.X + 1, last.Z - entry),
        };

        foreach (var step in path)
        {
            if (_cells.ContainsKey(step) || Neighbours(step, from, path, x0, z0, width, depth))
            {
                return false;
            }
        }

        if (!Free(x0, z0, width, depth, null, margin: 1))
        {
            return false;
        }

        corridor = NewRegion(RegionKind.Corridor);
        corridor.Cells.AddRange(path);
        corridor.Bends = bend ? 1 : 0;
        room = NewRegion(RegionKind.Room);
        (room.X0, room.Z0, room.Width, room.Depth) = (x0, z0, width, depth);
        room.Entrance = GridSides.Opposite(side);
        _doorEdges.Add(new MapEdge(start, side).Canonical);
        _doorEdges.Add(new MapEdge(last, side).Canonical);
        return true;
    }

    private bool Neighbours(GridCell cell, MapRegion from, List<GridCell> path, int x0, int z0, int width, int depth)
    {
        foreach (var side in GridSides.All)
        {
            var next = cell.Step(side);
            if (path.Contains(next) || (next.X >= x0 && next.X < x0 + width && next.Z >= z0 && next.Z < z0 + depth))
            {
                continue;
            }

            if (_cells.TryGetValue(next, out var owner) && !(ReferenceEquals(owner, from) && path.IndexOf(cell) == 0))
            {
                return true;
            }
        }

        return false;
    }

    private bool Free(int x0, int z0, int width, int depth, MapRegion? except, int margin)
    {
        for (var z = z0 - margin; z < z0 + depth + margin; z++)
        {
            for (var x = x0 - margin; x < x0 + width + margin; x++)
            {
                if (_cells.TryGetValue(new GridCell(x, z), out var owner) && !ReferenceEquals(owner, except))
                {
                    var inside = x >= x0 && x < x0 + width && z >= z0 && z < z0 + depth;
                    if (inside || owner.Kind == RegionKind.Room || !TouchesOnlyAtDoor(owner, except))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool TouchesOnlyAtDoor(MapRegion corridor, MapRegion? room)
    {
        if (room is null)
        {
            return false;
        }

        foreach (var doorway in Doorways)
        {
            if (ReferenceEquals(doorway.Corridor, corridor) && ReferenceEquals(doorway.Room, room))
            {
                return true;
            }
        }

        return false;
    }

    private void Commit(MapRegion room, MapRegion? corridor, MapRegion? from)
    {
        room.FillRectangle();
        Regions.Add(room);
        Rooms.Add(room);
        foreach (var cell in room.Cells)
        {
            _cells[cell] = room;
        }

        if (corridor is not null)
        {
            Regions.Add(corridor);
            foreach (var cell in corridor.Cells)
            {
                _cells[cell] = corridor;
            }

            foreach (var edge in _doorEdges)
            {
                var a = _cells.GetValueOrDefault(edge.Cell);
                var b = _cells.GetValueOrDefault(edge.Mirror.Cell);
                if (ReferenceEquals(a, corridor) || ReferenceEquals(b, corridor))
                {
                    var roomSide = ReferenceEquals(a, corridor) ? b : a;
                    if (roomSide is not null && !Doorways.Exists(d => d.Edge == edge))
                    {
                        Doorways.Add(new MapDoorway(roomSide, corridor, edge));
                    }
                }
            }
        }

        _ = from;
        Invalidate();
    }

    private List<MapWall> BuildWalls(MapRegion region)
    {
        var walls = new List<MapWall>();
        foreach (var cell in region.Cells)
        {
            foreach (var side in GridSides.All)
            {
                var next = cell.Step(side);
                if (_cells.TryGetValue(next, out var owner) && ReferenceEquals(owner, region))
                {
                    continue;
                }

                var edge = new MapEdge(cell, side);
                var canonical = edge.Canonical;
                if (_doorEdges.Contains(canonical))
                {
                    walls.Add(new MapWall(region, edge, region.Kind == RegionKind.Room ? WallKind.Doorway : WallKind.Shared, "wall_doorway"));
                    continue;
                }

                if (owner is not null && owner.Id < region.Id)
                {
                    walls.Add(new MapWall(region, edge, WallKind.Shared, "wall"));
                    continue;
                }

                if (region.Kind == RegionKind.Corridor)
                {
                    walls.Add(new MapWall(region, edge, WallKind.Decor, "wall"));
                    continue;
                }

                var roll = Roll(Settings.Seed, cell, side);
                if (roll < Settings.DecorWalls)
                {
                    var piece = DecorPieces[(int)(roll / Settings.DecorWalls * DecorPieces.Length) % DecorPieces.Length];
                    walls.Add(new MapWall(region, edge, WallKind.Decor, piece));
                }
                else
                {
                    walls.Add(new MapWall(region, edge, WallKind.Slot, "wall"));
                }
            }
        }

        if (region.Kind == RegionKind.Room)
        {
            foreach (var side in new[] { GridSides.Left(region.Entrance), GridSides.Right(region.Entrance) })
            {
                if (walls.Count(w => w.Kind == WallKind.Decor && w.Piece is not ("wall_window_open" or "wall_shelves")) >= 2)
                {
                    break;
                }

                var along = walls.Where(w => w.Kind == WallKind.Slot && w.Edge.Side == side).ToList();
                if (along.Count >= 3)
                {
                    var middle = along[along.Count / 2];
                    middle.Kind = WallKind.Decor;
                    middle.Piece = "wall_cracked";
                }
            }

            if (!walls.Exists(w => w.Kind == WallKind.Decor) && walls.Find(w => w.Kind == WallKind.Slot) is { } any)
            {
                any.Kind = WallKind.Decor;
                any.Piece = "wall_cracked";
            }
        }

        PlaceTorches(region, walls);
        return walls;
    }

    private void PlaceTorches(MapRegion region, List<MapWall> walls)
    {
        var placed = new List<MapWall>();
        foreach (var wall in walls)
        {
            if (wall.Kind != WallKind.Decor || wall.Piece is "wall_window_open" or "wall_shelves")
            {
                continue;
            }

            var center = GridSides.WallCenter(wall.Edge.Cell, wall.Edge.Side);
            var far = true;
            foreach (var other in placed)
            {
                var distance = (GridSides.WallCenter(other.Edge.Cell, other.Edge.Side) - center).Length() / 4;
                if (distance < Settings.TorchSpacing)
                {
                    far = false;
                    break;
                }
            }

            if (far)
            {
                wall.Torch = true;
                placed.Add(wall);
            }
        }

        var needed = region.Kind == RegionKind.Corridor ? 1 + region.Bends : 2;
        foreach (var wall in walls)
        {
            if (placed.Count >= needed)
            {
                break;
            }

            if (!wall.Torch && wall.Kind == WallKind.Decor)
            {
                wall.Torch = true;
                placed.Add(wall);
            }
        }
    }

    internal static double Roll(int seed, GridCell cell, GridSide side)
    {
        var hash = StableHash.Of(seed, cell.X, cell.Z, (int)side);
        return (hash & 0xFFFFFF) / (double)0x1000000;
    }
}
