using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class FallApartEffect : IMeshTransform
{
    public const int DefaultBlockSize = 40;

    private const string NodeName = "fallapart";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly MeshGrid _grid = new();
    private readonly int _blockSize;
    private EffectTimeline _timeline;
    private double _progress;
    private double _eased;
    private SceneTransform? _node;

    public FallApartEffect(int blockSize = DefaultBlockSize) => _blockSize = Math.Clamp(blockSize, 1, 100000);

    public int BlockSize => _blockSize;

    public bool IsRunning => _node is { IsDestroyed: false };

    public bool Begin(TransformStack stack, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    public bool Begin(TransformStack stack, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled)
        {
            return false;
        }

        _progress = 0;
        _eased = 0;
        _node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Effect, NodeName);
        _timeline.Easing = EasingCurve.Linear;
        _timeline.Start(duration.Nanos);
        _node.Deformer = this;
        _node.Alpha = 1f;
        _node.NotifyDeformed();
        return true;
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (_node is not { IsDestroyed: false } node)
        {
            return false;
        }

        _progress = _timeline.Progress(tick);
        _eased = _progress * _progress * _progress;
        node.Alpha = (float)Math.Clamp(1.0 - _eased, 0, 1);
        node.NotifyDeformed();
        return _timeline.Running(tick);
    }

    public void End(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (_node is { IsDestroyed: false } node)
        {
            node.Deformer = null;
        }

        stack.Remove(NodeName);
        _node = null;
    }

    public Box MapBounds(in Box childBounds)
    {
        var reach = (int)Math.Ceiling(_eased * 64 * 110) + Math.Max(childBounds.Width, childBounds.Height);
        return new Box(
            childBounds.X - reach,
            childBounds.Y - reach,
            childBounds.Width + (2 * reach),
            childBounds.Height + (2 * reach));
    }

    public int VertexCount(in Box childBounds)
    {
        _grid.Layout(childBounds, _blockSize);
        return _grid.VertexCount;
    }

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        _grid.Layout(childBounds, _blockSize);
        var white = new RenderColor(1f, 1f, 1f, 1f);
        var width = (double)childBounds.Width;
        var height = (double)childBounds.Height;
        var modif = _eased * 64;
        for (var cell = 0; cell < _grid.CellCount; cell++)
        {
            _grid.CellSource(cell, out var left, out var top, out var right, out var bottom);
            var localLeft = left - childBounds.X;
            var localTop = top - childBounds.Y;

            var random = new Xorshift(cell);
            var xdiff = 0.0;
            if (localLeft < width / 2)
            {
                xdiff = -((width / 2) - localLeft) / width * 100;
            }
            else if (localLeft > width / 2)
            {
                xdiff = (localLeft - (width / 2)) / width * 100;
            }

            var ydiff = 0.0;
            if (localTop < height / 2)
            {
                ydiff = -((height / 2) - localTop) / height * 100;
            }
            else if (localTop > height / 2)
            {
                ydiff = (localTop - (height / 2)) / height * 100;
            }

            xdiff += (long)(random.Next() % 21) - 10;
            ydiff += (long)(random.Next() % 21) - 10;
            var spin = (((long)(random.Next() % 720) - 360) / 360.0) * 2 * Math.PI * _progress;

            var moveX = xdiff * modif;
            var moveY = ydiff * modif;
            Span<double> cornerX = [left + moveX, right + moveX, right + moveX, left + moveX];
            Span<double> cornerY = [top + moveY, top + moveY, bottom + moveY, bottom + moveY];
            var centerX = (cornerX[0] + cornerX[1] + cornerX[2] + cornerX[3]) / 4;
            var centerY = (cornerY[0] + cornerY[1] + cornerY[2] + cornerY[3]) / 4;
            var sin = Math.Sin(spin);
            var cos = Math.Cos(spin);
            for (var corner = 0; corner < 4; corner++)
            {
                var dx = cornerX[corner] - centerX;
                var dy = cornerY[corner] - centerY;
                cornerX[corner] = centerX + ((dx * cos) - (dy * sin));
                cornerY[corner] = centerY + ((dx * sin) + (dy * cos));
            }

            MeshGrid.WriteCell(
                into.Slice(cell * 6, 6),
                left,
                top,
                right,
                bottom,
                ((float)cornerX[0], (float)cornerY[0]),
                ((float)cornerX[1], (float)cornerY[1]),
                ((float)cornerX[2], (float)cornerY[2]),
                ((float)cornerX[3], (float)cornerY[3]),
                white);
        }
    }

    private struct Xorshift(int cell)
    {
        private ulong _state = ((ulong)cell + 1) * 0x9E3779B97F4A7C15;

        public ulong Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return _state;
        }
    }
}
