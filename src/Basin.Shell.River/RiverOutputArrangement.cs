namespace Basin.Shell.River;

public sealed class RiverOutputArrangement
{
    private readonly List<IOutput> _order = [];

    public Func<IOutput, Point?>? Placement { get; set; }

    public IReadOnlyList<IOutput> Outputs => _order;

    internal void Add(IOutput output)
    {
        if (!_order.Contains(output))
        {
            _order.Add(output);
        }
    }

    internal void Remove(IOutput output) => _order.Remove(output);

    internal void Arrange(OutputLayout layout)
    {
        var cursor = 0;
        var placed = new List<Box>(_order.Count);
        foreach (var output in _order)
        {
            var mode = output.CurrentMode;
            var width = Math.Max(1, (int)Math.Ceiling(mode.Width / output.Scale));
            var height = Math.Max(1, (int)Math.Ceiling(mode.Height / output.Scale));

            var requested = Placement?.Invoke(output);
            var box = requested is { } point
                ? new Box(point.X, point.Y, width, height)
                : new Box(cursor, 0, width, height);

            var moved = true;
            while (moved)
            {
                moved = false;
                foreach (var other in placed)
                {
                    if (!other.Intersect(box).IsEmpty)
                    {
                        box = box with { X = other.Right };
                        moved = true;
                    }
                }
            }

            placed.Add(box);
            cursor = Math.Max(cursor, box.Right);
            layout.Move(output, box.X, box.Y);
        }
    }

    internal bool IsDisjoint(OutputLayout layout)
    {
        for (var i = 0; i < _order.Count; i++)
        {
            for (var j = i + 1; j < _order.Count; j++)
            {
                if (!layout.BoxOf(_order[i]).Intersect(layout.BoxOf(_order[j])).IsEmpty)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
