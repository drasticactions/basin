namespace Basin.Physics;

/// <summary>
/// Keeps one static wall body in a <see cref="PhysicsWorld"/> around a set of rectangles. It adds fences over the dead zones in the bounding box that no rectangle covers.
/// </summary>
public sealed class PhysicsWalls
{
    /// <summary>
    /// The thickness of the outer wall, in logical pixels. A fast body cannot pass through it in one step.
    /// </summary>
    public const int Thickness = 1000;

    private readonly PhysicsWorld _world;
    private readonly List<Box> _fences = [];
    private readonly List<int> _xs = [];
    private readonly List<int> _ys = [];
    private PhysicsBody _body;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhysicsWalls"/> class. It makes no wall until <see cref="Rebuild(ReadOnlySpan{Box})"/>.
    /// </summary>
    /// <param name="world">The world that holds the walls.</param>
    public PhysicsWalls(PhysicsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>
    /// Gets the bounding box of the rectangles from the last rebuild. It is empty when no wall exists.
    /// </summary>
    public Box Bounds { get; private set; }

    /// <summary>
    /// Gets the fences from the last rebuild. The list is live and changes on the next rebuild.
    /// </summary>
    public IReadOnlyList<Box> Fences => _fences;

    /// <summary>
    /// Gets the wall body, which impacts with a wall name. It is <see langword="default"/> when no wall exists.
    /// </summary>
    public PhysicsBody Body => _body;

    /// <summary>
    /// Replaces the walls with walls around the boxes of the outputs in a layout. A consumer calls it again on hotplug and on a change of mode, scale or transform.
    /// </summary>
    /// <param name="layout">The output layout. Outputs with an empty box are skipped.</param>
    public void Rebuild(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Span<Box> boxes = stackalloc Box[16];
        var count = 0;
        var list = boxes.Length >= layout.Outputs.Length ? boxes : new Box[layout.Outputs.Length];
        foreach (var (output, _) in layout.Outputs)
        {
            var box = layout.BoxOf(output);
            if (!box.IsEmpty)
            {
                list[count++] = box;
            }
        }

        Rebuild(list[..count]);
    }

    /// <summary>
    /// Replaces the walls with an outer wall on <see cref="PhysicsCategory.Edges"/> and fences on <see cref="PhysicsCategory.Fences"/>. An empty span only removes the walls.
    /// </summary>
    /// <param name="areas">The rectangles that bodies can occupy, in logical pixels.</param>
    public void Rebuild(ReadOnlySpan<Box> areas)
    {
        Clear();
        if (areas.IsEmpty)
        {
            return;
        }

        var bounds = areas[0];
        for (var i = 1; i < areas.Length; i++)
        {
            bounds = Union(bounds, areas[i]);
        }

        Bounds = bounds;
        _body = _world.CreateWall();
        _world.AddWallBox(_body, new Box(bounds.X - Thickness, bounds.Y - Thickness, Thickness, bounds.Height + (2 * Thickness)), PhysicsCategory.Edges);
        _world.AddWallBox(_body, new Box(bounds.Right, bounds.Y - Thickness, Thickness, bounds.Height + (2 * Thickness)), PhysicsCategory.Edges);
        _world.AddWallBox(_body, new Box(bounds.X, bounds.Y - Thickness, bounds.Width, Thickness), PhysicsCategory.Edges);
        _world.AddWallBox(_body, new Box(bounds.X, bounds.Bottom, bounds.Width, Thickness), PhysicsCategory.Edges);

        DeadZones(areas, bounds);
        foreach (var fence in _fences)
        {
            _world.AddWallBox(_body, fence, PhysicsCategory.Fences);
        }
    }

    /// <summary>
    /// Destroys the wall body and clears <see cref="Bounds"/> and <see cref="Fences"/>.
    /// </summary>
    public void Clear()
    {
        _fences.Clear();
        Bounds = default;
        if (_body.IsValid)
        {
            _world.Destroy(_body);
        }

        _body = default;
    }

    private void DeadZones(ReadOnlySpan<Box> areas, in Box bounds)
    {
        _xs.Clear();
        _ys.Clear();
        _xs.Add(bounds.X);
        _xs.Add(bounds.Right);
        _ys.Add(bounds.Y);
        _ys.Add(bounds.Bottom);
        foreach (var area in areas)
        {
            _xs.Add(area.X);
            _xs.Add(area.Right);
            _ys.Add(area.Y);
            _ys.Add(area.Bottom);
        }

        Distinct(_xs);
        Distinct(_ys);
        for (var row = 0; row + 1 < _ys.Count; row++)
        {
            var top = _ys[row];
            var bottom = _ys[row + 1];
            var start = -1;
            for (var column = 0; column + 1 < _xs.Count; column++)
            {
                var cell = new Box(_xs[column], top, _xs[column + 1] - _xs[column], bottom - top);
                var dead = !Covered(areas, cell);
                if (dead && start < 0)
                {
                    start = column;
                }

                if (!dead && start >= 0)
                {
                    AddFence(_xs[start], top, _xs[column], bottom);
                    start = -1;
                }
            }

            if (start >= 0)
            {
                AddFence(_xs[start], top, _xs[^1], bottom);
            }
        }
    }

    private void AddFence(int left, int top, int right, int bottom)
    {
        var fence = new Box(left, top, right - left, bottom - top);
        for (var i = 0; i < _fences.Count; i++)
        {
            var above = _fences[i];
            if (above.X == fence.X && above.Width == fence.Width && above.Bottom == fence.Y)
            {
                _fences[i] = new Box(above.X, above.Y, above.Width, above.Height + fence.Height);
                return;
            }
        }

        _fences.Add(fence);
    }

    private static bool Covered(ReadOnlySpan<Box> areas, in Box cell)
    {
        var centerX = cell.X + (cell.Width / 2.0);
        var centerY = cell.Y + (cell.Height / 2.0);
        foreach (var area in areas)
        {
            if (centerX >= area.X && centerX < area.Right && centerY >= area.Y && centerY < area.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    private static void Distinct(List<int> values)
    {
        values.Sort();
        var write = 0;
        for (var read = 0; read < values.Count; read++)
        {
            if (write == 0 || values[write - 1] != values[read])
            {
                values[write++] = values[read];
            }
        }

        values.RemoveRange(write, values.Count - write);
    }

    private static Box Union(in Box a, in Box b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new Box(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }
}
