using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// The fall-apart close animation. The window breaks into blocks that fly outward, spin and fade.
/// </summary>
public sealed class FallApartEffect : IMeshTransform
{
    /// <summary>
    /// The default block size in pixels.
    /// </summary>
    public const int DefaultBlockSize = 40;

    private const string NodeName = "fallapart";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly MeshGrid _grid = new();
    private readonly int _blockSize;
    private EffectTimeline _timeline;
    private double _progress;
    private double _eased;
    private SceneTransform? _node;

    /// <summary>
    /// Initializes a new instance of the <see cref="FallApartEffect"/> class.
    /// </summary>
    /// <param name="blockSize">The side of one block in pixels. The constructor clamps it to at least 1.</param>
    public FallApartEffect(int blockSize = DefaultBlockSize) => _blockSize = Math.Clamp(blockSize, 1, 100000);

    /// <summary>
    /// Gets the side of one block in pixels.
    /// </summary>
    public int BlockSize => _blockSize;

    /// <summary>
    /// Gets a value indicating whether the effect holds a live transform node.
    /// </summary>
    public bool IsRunning => _node is { IsDestroyed: false };

    /// <summary>
    /// Starts the animation anchored exactly at a tick.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="now">The tick that anchors the start.</param>
    /// <param name="duration">The animation duration.</param>
    /// <returns><see langword="false"/> when the duration is disabled and nothing starts.</returns>
    public bool Begin(TransformStack stack, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    /// <summary>
    /// Starts the animation with an unanchored timeline, so the first step already moves. A second call reuses the node.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="duration">The animation duration.</param>
    /// <returns><see langword="false"/> when the duration is disabled and nothing starts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Advances the animation and redraws the mesh. The travel and the fade use a cubic of the progress, the spin uses the raw progress.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the animation needs more frames. The consumer then calls <see cref="End"/>.</returns>
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

    /// <summary>
    /// Removes the effect node from the stack.
    /// </summary>
    /// <param name="stack">The transform stack that <see cref="Begin(TransformStack, AnimationDuration)"/> took.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
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

    /// <inheritdoc/>
    public Box MapBounds(in Box childBounds)
    {
        var reach = (int)Math.Ceiling(_eased * 64 * 110) + Math.Max(childBounds.Width, childBounds.Height);
        return new Box(
            childBounds.X - reach,
            childBounds.Y - reach,
            childBounds.Width + (2 * reach),
            childBounds.Height + (2 * reach));
    }

    /// <inheritdoc/>
    public int VertexCount(in Box childBounds)
    {
        _grid.Layout(childBounds, _blockSize);
        return _grid.VertexCount;
    }

    /// <inheritdoc/>
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
