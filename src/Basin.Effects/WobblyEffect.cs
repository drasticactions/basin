using Basin.Scene;

namespace Basin.Effects;

public sealed class WobblyEffect : IMeshTransform
{
    private const int GridSize = 4;
    private const int ObjectCount = GridSize * GridSize;
    private const double Mass = 15.0;
    private const double StepMillis = 15.0;
    private const double VelocityThreshold = 0.5;
    private const double ForceThreshold = 20.0;

    private readonly double[] _posX = new double[ObjectCount];
    private readonly double[] _posY = new double[ObjectCount];
    private readonly double[] _velX = new double[ObjectCount];
    private readonly double[] _velY = new double[ObjectCount];
    private readonly double[] _forceX = new double[ObjectCount];
    private readonly double[] _forceY = new double[ObjectCount];
    private readonly bool[] _immobile = new bool[ObjectCount];
    private readonly (int A, int B, double OffX, double OffY)[] _springs = new (int, int, double, double)[24];

    private readonly int _resolution;
    private readonly double _friction;
    private readonly double _springK;
    private double[] _gridX = [];
    private double[] _gridY = [];

    private Box _bounds;
    private bool _sized;
    private double _stepCarry;
    private long _lastNanos;
    private bool _hasLast;
    private int _grabbed = -1;
    private bool _tiled;
    private bool _wobbly;
    private SceneTransform? _node;

    public WobblyEffect(WobblyOptions options = default)
    {
        var resolved = options == default ? new WobblyOptions() : options;
        _resolution = Math.Clamp(resolved.GridResolution, 1, 64);
        _friction = Math.Clamp(resolved.Friction, 0.1, 10.0);
        _springK = Math.Clamp(resolved.SpringK, 0.1, 10.0);
        BuildSprings();
    }

    public bool IsAttached => _node is { IsDestroyed: false };

    public bool IsWobbling => _wobbly || _grabbed >= 0;

    public void Attach(TransformStack stack)
    {
        _node = stack.Get("wobbly") ?? stack.Add(TransformStack.ZOrder.Effect, "wobbly");
        _sized = false;
        _hasLast = false;
    }

    public void Detach()
    {
        if (_node is { IsDestroyed: false } node)
        {
            node.Deformer = null;
        }

        _node = null;
    }

    public void Grab(double localX, double localY)
    {
        if (_node is not { IsDestroyed: false } node)
        {
            return;
        }

        EnsureSized(node.ContentBounds);
        var nearest = 0;
        var best = double.MaxValue;
        for (var i = 0; i < ObjectCount; i++)
        {
            var dx = _posX[i] - localX;
            var dy = _posY[i] - localY;
            var distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        _grabbed = nearest;
        _immobile[nearest] = true;
        Kick(0.05);
        Wake(node);
    }

    public void Release()
    {
        if (_grabbed >= 0)
        {
            _immobile[_grabbed] = false;
            _grabbed = -1;
        }

        if (_tiled)
        {
            PinCorners();
        }

        if (_node is { IsDestroyed: false } node && _sized)
        {
            Wake(node);
        }
    }

    public void Activate()
    {
        if (_node is not { IsDestroyed: false } node)
        {
            return;
        }

        EnsureSized(node.ContentBounds);
        Kick(0.05);
        Wake(node);
    }

    public void SetTiled(bool tiled)
    {
        _tiled = tiled;
        for (var i = 0; i < ObjectCount; i++)
        {
            _immobile[i] = i == _grabbed;
        }

        if (tiled)
        {
            PinCorners();
        }
    }

    public void NotifyMoved(int dx, int dy)
    {
        if (!_sized || (dx == 0 && dy == 0))
        {
            return;
        }

        for (var i = 0; i < ObjectCount; i++)
        {
            if (!_immobile[i])
            {
                _posX[i] -= dx;
                _posY[i] -= dy;
            }
        }

        if (_node is { IsDestroyed: false } node)
        {
            Wake(node);
        }
    }

    public bool Step(in FrameTick tick)
    {
        if (_node is not { IsDestroyed: false } node || !_sized)
        {
            return false;
        }

        if (!_hasLast)
        {
            _lastNanos = tick.TargetPresentNanos;
            _hasLast = true;
            return IsWobbling;
        }

        var elapsedMillis = (tick.TargetPresentNanos - _lastNanos) / 1_000_000.0;
        _lastNanos = tick.TargetPresentNanos;
        if (elapsedMillis <= 0)
        {
            if (IsWobbling)
            {
                node.NotifyDeformed();
            }

            return IsWobbling;
        }

        node.NotifyDeformed();
        _stepCarry += elapsedMillis / StepMillis;
        var steps = (int)Math.Floor(_stepCarry);
        _stepCarry -= steps;

        double velocitySum = 0, forceSum = 0;
        for (var step = 0; step < steps; step++)
        {
            (velocitySum, forceSum) = Integrate();
        }

        if (steps > 0)
        {
            _wobbly = velocitySum > VelocityThreshold || forceSum > ForceThreshold;
        }

        node.NotifyDeformed();
        if (!IsWobbling)
        {
            node.Deformer = null;
            return false;
        }

        node.Deformer = this;
        return true;
    }

    public Box MapBounds(in Box childBounds)
    {
        if (!_sized)
        {
            return childBounds;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i < ObjectCount; i++)
        {
            minX = Math.Min(minX, _posX[i]);
            minY = Math.Min(minY, _posY[i]);
            maxX = Math.Max(maxX, _posX[i]);
            maxY = Math.Max(maxY, _posY[i]);
        }

        var left = (int)Math.Floor(minX);
        var top = (int)Math.Floor(minY);
        var hull = new Box(left, top, (int)Math.Ceiling(maxX) - left, (int)Math.Ceiling(maxY) - top);
        var x0 = Math.Min(hull.X, childBounds.X);
        var y0 = Math.Min(hull.Y, childBounds.Y);
        var x1 = Math.Max(hull.Right, childBounds.Right);
        var y1 = Math.Max(hull.Bottom, childBounds.Bottom);
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }

    public int VertexCount(in Box childBounds) => _resolution * _resolution * 6;

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        EnsureSized(childBounds);
        var points = _resolution + 1;
        if (_gridX.Length < points * points)
        {
            _gridX = new double[points * points];
            _gridY = new double[points * points];
        }

        for (var j = 0; j < points; j++)
        {
            for (var i = 0; i < points; i++)
            {
                var (x, y) = EvaluatePatch((double)i / _resolution, (double)j / _resolution);
                _gridX[(j * points) + i] = x;
                _gridY[(j * points) + i] = y;
            }
        }

        var white = new RenderColor(1f, 1f, 1f, 1f);
        var write = 0;
        for (var j = 0; j < _resolution; j++)
        {
            for (var i = 0; i < _resolution; i++)
            {
                Span<(int I, int J)> corners = [(i, j), (i + 1, j), (i, j + 1), (i + 1, j), (i + 1, j + 1), (i, j + 1)];
                foreach (var (ci, cj) in corners)
                {
                    var index = (cj * points) + ci;
                    var u = childBounds.X + ((double)ci / _resolution * childBounds.Width);
                    var v = childBounds.Y + ((double)cj / _resolution * childBounds.Height);
                    into[write++] = new MeshVertex(
                        (float)_gridX[index], (float)_gridY[index], (float)u, (float)v, white);
                }
            }
        }
    }

    private (double VelocitySum, double ForceSum) Integrate()
    {
        foreach (var (a, b, offX, offY) in _springs)
        {
            var daX = 0.5 * (_posX[b] - _posX[a] - offX);
            var daY = 0.5 * (_posY[b] - _posY[a] - offY);
            _forceX[a] += _springK * daX;
            _forceY[a] += _springK * daY;
            _forceX[b] -= _springK * daX;
            _forceY[b] -= _springK * daY;
        }

        if (_grabbed < 0 && !_tiled)
        {
            var restoreK = _springK * 0.5;
            for (var i = 0; i < ObjectCount; i++)
            {
                var row = i / GridSize;
                var column = i % GridSize;
                var restX = _bounds.X + ((double)column / (GridSize - 1) * _bounds.Width);
                var restY = _bounds.Y + ((double)row / (GridSize - 1) * _bounds.Height);
                _forceX[i] += restoreK * (restX - _posX[i]);
                _forceY[i] += restoreK * (restY - _posY[i]);
            }
        }

        double velocitySum = 0, forceSum = 0;
        for (var i = 0; i < ObjectCount; i++)
        {
            if (_immobile[i])
            {
                _velX[i] = 0;
                _velY[i] = 0;
                _forceX[i] = 0;
                _forceY[i] = 0;
                continue;
            }

            _forceX[i] -= _friction * _velX[i];
            _forceY[i] -= _friction * _velY[i];
            _velX[i] += _forceX[i] / Mass;
            _velY[i] += _forceY[i] / Mass;
            _posX[i] += _velX[i];
            _posY[i] += _velY[i];
            velocitySum += Math.Abs(_velX[i]) + Math.Abs(_velY[i]);
            forceSum += Math.Abs(_forceX[i]) + Math.Abs(_forceY[i]);
            _forceX[i] = 0;
            _forceY[i] = 0;
        }

        return (velocitySum, forceSum);
    }

    private void EnsureSized(in Box bounds)
    {
        if (_sized && bounds == _bounds)
        {
            return;
        }

        if (!_sized || _bounds.Width <= 0 || _bounds.Height <= 0)
        {
            for (var j = 0; j < GridSize; j++)
            {
                for (var i = 0; i < GridSize; i++)
                {
                    var index = (j * GridSize) + i;
                    _posX[index] = bounds.X + ((double)i / (GridSize - 1) * bounds.Width);
                    _posY[index] = bounds.Y + ((double)j / (GridSize - 1) * bounds.Height);
                    _velX[index] = 0;
                    _velY[index] = 0;
                    _forceX[index] = 0;
                    _forceY[index] = 0;
                }
            }
        }
        else
        {
            for (var i = 0; i < ObjectCount; i++)
            {
                _posX[i] = bounds.X + ((_posX[i] - _bounds.X) * bounds.Width / _bounds.Width);
                _posY[i] = bounds.Y + ((_posY[i] - _bounds.Y) * bounds.Height / _bounds.Height);
            }
        }

        _bounds = bounds;
        _sized = true;
        BuildSprings();
    }

    private void BuildSprings()
    {
        var restX = _bounds.Width / (double)(GridSize - 1);
        var restY = _bounds.Height / (double)(GridSize - 1);
        var write = 0;
        for (var j = 0; j < GridSize; j++)
        {
            for (var i = 0; i < GridSize; i++)
            {
                var index = (j * GridSize) + i;
                if (i + 1 < GridSize)
                {
                    _springs[write++] = (index, index + 1, restX, 0);
                }

                if (j + 1 < GridSize)
                {
                    _springs[write++] = (index, index + GridSize, 0, restY);
                }
            }
        }
    }

    private void PinCorners()
    {
        _immobile[0] = true;
        _immobile[GridSize - 1] = true;
        _immobile[ObjectCount - GridSize] = true;
        _immobile[ObjectCount - 1] = true;
    }

    private void Kick(double strength)
    {
        foreach (var (a, b, offX, offY) in _springs)
        {
            _velX[b] -= offX * strength;
            _velY[b] -= offY * strength;
            _ = a;
        }
    }

    private void Wake(SceneTransform node)
    {
        _wobbly = true;
        node.Deformer = this;
        node.NotifyDeformed();
    }

    private (double X, double Y) EvaluatePatch(double u, double v)
    {
        Span<double> bu = stackalloc double[4];
        Span<double> bv = stackalloc double[4];
        Bernstein(u, bu);
        Bernstein(v, bv);
        double x = 0, y = 0;
        for (var j = 0; j < GridSize; j++)
        {
            for (var i = 0; i < GridSize; i++)
            {
                var weight = bu[i] * bv[j];
                x += weight * _posX[(j * GridSize) + i];
                y += weight * _posY[(j * GridSize) + i];
            }
        }

        return (x, y);
    }

    private static void Bernstein(double t, Span<double> into)
    {
        var inverse = 1 - t;
        into[0] = inverse * inverse * inverse;
        into[1] = 3 * inverse * inverse * t;
        into[2] = 3 * inverse * t * t;
        into[3] = t * t * t;
    }
}
