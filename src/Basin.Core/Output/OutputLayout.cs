using System.Runtime.InteropServices;

namespace Basin;

/// <summary>
/// Places outputs in one shared space of logical coordinates, so the pointer and windows can move between them. Each output covers its logical size from <see cref="OutputScaling.LogicalSize"/>. The layout holds geometry only, no policy.
/// </summary>
public sealed class OutputLayout
{
    private readonly List<Entry> _entries = [];
    private readonly List<(IOutput Output, Point Position)> _projection = [];
    private bool _projectionStale = true;

    /// <summary>
    /// Occurs when an output is added, moved or removed, or when a commit changes the logical box of an output in the layout. A move to the current position does not raise it.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets each output in the layout with the logical position of its top-left corner, in the order of addition. The span is valid until the next change to the layout.
    /// </summary>
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

    /// <summary>
    /// Gets a text key built from the name and the logical box of each output. Two layouts with the same key have the same geometry. Each read allocates a new string.
    /// </summary>
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

    /// <summary>
    /// Adds an output at a logical position, or moves it there when it is already in the layout. The layout removes the output when it is destroyed.
    /// </summary>
    /// <param name="output">The output to add.</param>
    /// <param name="x">The horizontal position of the top-left corner.</param>
    /// <param name="y">The vertical position of the top-left corner.</param>
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

    /// <summary>
    /// Moves an output in the layout to a new logical position. It does nothing when the output is not in the layout or already at that position.
    /// </summary>
    /// <param name="output">The output to move.</param>
    /// <param name="x">The new horizontal position of the top-left corner.</param>
    /// <param name="y">The new vertical position of the top-left corner.</param>
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

    /// <summary>
    /// Removes an output from the layout. It does nothing when the output is not in the layout.
    /// </summary>
    /// <param name="output">The output to remove.</param>
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

    /// <summary>
    /// Tells whether the layout carries an output. An output out of the layout gets no pointer, no surface enter and no layout events.
    /// </summary>
    /// <param name="output">The output to look for.</param>
    /// <returns><see langword="true"/> when the output is in the layout.</returns>
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

    /// <summary>
    /// Gets the smallest logical box that contains every output, or an empty box when the layout has no outputs. Each read computes it from the live modes and scales.
    /// </summary>
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

    /// <summary>
    /// Gets the logical box of an output from its position and its current logical size.
    /// </summary>
    /// <param name="output">The output to look up.</param>
    /// <returns>The box, or an empty box when the output is not in the layout.</returns>
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

    /// <summary>
    /// Packs outputs from left to right in the given order at y zero, with no gaps. Outputs that are not in the layout are skipped. Run it again after a change to a mode, a scale or a transform.
    /// </summary>
    /// <param name="outputs">The outputs in the order from left to right.</param>
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

    /// <summary>
    /// Converts a point in the panel pixels of an output to layout coordinates. It applies the output transform and divides by the scale.
    /// </summary>
    /// <param name="output">The output that the point is on.</param>
    /// <param name="physicalX">The horizontal position in panel pixels.</param>
    /// <param name="physicalY">The vertical position in panel pixels.</param>
    /// <returns>The point in logical layout coordinates.</returns>
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

    /// <summary>
    /// Converts a normalized point on the panel of an output to layout coordinates, for example from a touchscreen. It applies the output transform.
    /// </summary>
    /// <param name="output">The output that the point is on.</param>
    /// <param name="normalizedX">The horizontal position on the panel from 0 to 1.</param>
    /// <param name="normalizedY">The vertical position on the panel from 0 to 1.</param>
    /// <returns>The point in logical layout coordinates.</returns>
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

    /// <summary>
    /// Finds the output whose logical box contains a point. The first output in layout order wins where boxes overlap.
    /// </summary>
    /// <param name="x">The horizontal layout position.</param>
    /// <param name="y">The vertical layout position.</param>
    /// <returns>The output, or <see langword="null"/> when no output contains the point.</returns>
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

    /// <summary>
    /// Finds the point on an output that is nearest to a layout point. A point on an output comes back unchanged.
    /// </summary>
    /// <param name="x">The horizontal layout position.</param>
    /// <param name="y">The vertical layout position.</param>
    /// <returns>The nearest point, or the same point when the layout has no output with a size.</returns>
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
