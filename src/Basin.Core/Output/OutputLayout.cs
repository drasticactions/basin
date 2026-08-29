using System.Runtime.InteropServices;

namespace Basin;

public sealed class OutputLayout
{
    private readonly List<Entry> _entries = [];
    private readonly List<(IOutput Output, Point Position)> _projection = [];
    private bool _projectionStale = true;

    public event Action? Changed;

    public ReadOnlySpan<(IOutput Output, Point Position)> Outputs
    {
        get
        {
            if (_projectionStale)
            {
                _projection.Clear();
                foreach (var entry in _entries)
                {
                    _projection.Add((entry.Output, entry.Position));
                }

                _projectionStale = false;
            }

            return CollectionsMarshal.AsSpan(_projection);
        }
    }

    public string Id
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            foreach (var entry in _entries)
            {
                var box = BoxOf(entry.Output);
                builder.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{entry.Output.Name}:{box.X},{box.Y},{box.Width},{box.Height};");
            }

            return builder.ToString();
        }
    }

    public void Add(IOutput output, int x, int y)
    {
        TryRemove(output);
        var entry = new Entry(output, new Point(x, y));
        _entries.Add(entry);
        _projectionStale = true;
        output.Destroyed += entry.OnDestroyed = () => Remove(output);
        output.Committed += entry.OnCommitted = fields => OnOutputCommitted(entry, fields);
        entry.LastBox = entry.Box;
        OutputGlobal.For(output)?.NotifyPosition(x, y);
        Changed?.Invoke();
    }

    public void Move(IOutput output, int x, int y)
    {
        foreach (var entry in _entries)
        {
            if (entry.Output == output)
            {
                if (entry.Position.X == x && entry.Position.Y == y)
                {
                    return;
                }

                entry.Position = new Point(x, y);
                _projectionStale = true;
                OutputGlobal.For(output)?.NotifyPosition(x, y);
                Changed?.Invoke();
                return;
            }
        }
    }

    public void Remove(IOutput output)
    {
        if (TryRemove(output))
        {
            Changed?.Invoke();
        }
    }

    private bool TryRemove(IOutput output)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Output == output)
            {
                if (_entries[i].OnDestroyed is { } handler)
                {
                    output.Destroyed -= handler;
                }

                if (_entries[i].OnCommitted is { } committed)
                {
                    output.Committed -= committed;
                }

                _entries.RemoveAt(i);
                _projectionStale = true;
                return true;
            }
        }

        return false;
    }

    private void OnOutputCommitted(Entry entry, OutputStateFields fields)
    {
        const OutputStateFields Resized =
            OutputStateFields.Mode | OutputStateFields.Scale | OutputStateFields.Transform;
        if ((fields & Resized) == 0)
        {
            return;
        }

        var box = entry.Box;
        if (box == entry.LastBox)
        {
            return;
        }

        entry.LastBox = box;
        Changed?.Invoke();
    }

    public bool Contains(IOutput output)
    {
        foreach (var entry in _entries)
        {
            if (entry.Output == output)
            {
                return true;
            }
        }

        return false;
    }

    public Box Bounds
    {
        get
        {
            if (_entries.Count == 0)
            {
                return default;
            }

            var first = _entries[0].Box;
            var x1 = first.X;
            var y1 = first.Y;
            var x2 = first.Right;
            var y2 = first.Bottom;
            for (var i = 1; i < _entries.Count; i++)
            {
                var box = _entries[i].Box;
                x1 = Math.Min(x1, box.X);
                y1 = Math.Min(y1, box.Y);
                x2 = Math.Max(x2, box.Right);
                y2 = Math.Max(y2, box.Bottom);
            }

            return new Box(x1, y1, x2 - x1, y2 - y1);
        }
    }

    public Box BoxOf(IOutput output)
    {
        foreach (var entry in _entries)
        {
            if (entry.Output == output)
            {
                return entry.Box;
            }
        }

        return default;
    }

    public void ArrangeHorizontally(IEnumerable<IOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        var x = 0;
        foreach (var output in outputs)
        {
            if (!Contains(output))
            {
                continue;
            }

            Move(output, x, 0);
            x += output.LogicalSize().Width;
        }
    }

    public (double X, double Y) ToLayout(IOutput output, double physicalX, double physicalY)
    {
        ArgumentNullException.ThrowIfNull(output);
        var box = BoxOf(output);
        var scale = output.Scale;
        var (x, y) = (physicalX, physicalY);
        if (output.Transform != OutputTransform.Normal)
        {
            var mode = output.CurrentMode;
            (x, y) = output.Transform.ToMatrix(mode.Width, mode.Height).Map(physicalX, physicalY);
        }

        return (box.X + (x / scale), box.Y + (y / scale));
    }

    public (double X, double Y) FromNormalized(IOutput output, double normalizedX, double normalizedY)
    {
        ArgumentNullException.ThrowIfNull(output);
        var box = BoxOf(output);
        var (x, y) = (normalizedX, normalizedY);
        if (output.Transform != OutputTransform.Normal)
        {
            (x, y) = output.Transform.ToMatrix(1, 1).Map(normalizedX, normalizedY);
        }

        return (box.X + (x * box.Width), box.Y + (y * box.Height));
    }

    public IOutput? OutputAt(double x, double y)
    {
        foreach (var entry in _entries)
        {
            var box = entry.Box;
            if (x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom)
            {
                return entry.Output;
            }
        }

        return null;
    }

    public (double X, double Y) ClosestPoint(double x, double y)
    {
        if (OutputAt(x, y) is not null)
        {
            return (x, y);
        }

        var best = (X: x, Y: y);
        var bestDistance = double.MaxValue;
        foreach (var entry in _entries)
        {
            var box = entry.Box;
            if (box.IsEmpty)
            {
                continue;
            }

            var cx = Math.Clamp(x, box.X, box.Right - 1);
            var cy = Math.Clamp(y, box.Y, box.Bottom - 1);
            var distance = (cx - x) * (cx - x) + (cy - y) * (cy - y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (cx, cy);
            }
        }

        return best;
    }

    private sealed class Entry(IOutput output, Point position)
    {
        public IOutput Output { get; } = output;

        public Point Position { get; set; } = position;

        public Action? OnDestroyed { get; set; }

        public Action<OutputStateFields>? OnCommitted { get; set; }

        public Box LastBox { get; set; }

        public Box Box
        {
            get
            {
                var (width, height) = Output.LogicalSize();
                return new Box(Position.X, Position.Y, width, height);
            }
        }
    }
}
