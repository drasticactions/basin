using Basin;
using Basin.Scene;

namespace TinyComp;

internal sealed class FrameCornerRig : IDisposable
{
    private readonly Frame _frame;
    private readonly int _radius;
    private readonly Action _changed;
    private Box _lastOuter;
    private bool _disposed;

    public FrameCornerRig(Frame frame, SceneBuffer content, int radius, Action changed)
    {
        _frame = frame;
        _radius = radius;
        _changed = changed;
        _ = content;
        _frame.Committed += Update;
        Update();
    }

    public CornerClip ContentClip { get; private set; }

    private void Update()
    {
        if (_disposed || _frame.IsFaulted)
        {
            return;
        }

        var outer = _frame.OuterBounds;
        if (outer.IsEmpty || outer == _lastOuter)
        {
            return;
        }

        var strips = _frame.StripNodes;
        for (var i = 0; i < 4; i++)
        {
            var strip = _frame.StripBounds(i);
            strips[i].Corners = new CornerClip(_radius, outer.Width, outer.Height, strip.X, strip.Y);
        }

        var insets = _frame.Insets;
        ContentClip = new CornerClip(_radius, outer.Width, outer.Height, insets.Left, insets.Top);
        _lastOuter = outer;
        _changed();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frame.Committed -= Update;
        var strips = _frame.StripNodes;
        for (var i = 0; i < 4; i++)
        {
            if (!strips[i].IsDestroyed)
            {
                strips[i].Corners = default;
            }
        }
    }
}
