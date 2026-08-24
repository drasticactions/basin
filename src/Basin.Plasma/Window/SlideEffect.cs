using Basin.Effects;
using Basin.Scene;

namespace Basin.Plasma;

public sealed class SlideEffect : IDisposable
{
    private const string NodeName = "kde-slide";

    public const long DefaultDurationNanos = 250_000_000;

    private readonly SceneSurface _scene;
    private readonly SlideManager _manager;
    private readonly Func<Surface, Box> _screenArea;
    private readonly Action _onCommitted;
    private readonly Action _onCommitRequested;
    private readonly Action _onSurfaceDestroyed;
    private TransformStack? _stack;
    private SceneTransform? _inNode;
    private EffectTimeline _timeline;
    private double _dx;
    private double _dy;
    private bool _inRunning;
    private bool _outRunning;
    private bool _outPrepared;
    private int _outX;
    private int _outY;
    private int _outWidth;
    private int _outHeight;
    private SceneTransform? _outWrap;
    private SceneSnapshot? _snapshot;
    private bool _wasMapped;
    private bool _hadSlide;
    private bool _disposed;

    public SlideEffect(SceneSurface scene, SlideManager manager, Func<Surface, Box> screenArea)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(screenArea);
        _scene = scene;
        _manager = manager;
        _screenArea = screenArea;
        _onCommitted = OnCommitted;
        _onCommitRequested = OnCommitRequested;
        _onSurfaceDestroyed = OnSurfaceDestroyed;
        scene.Surface.Committed += _onCommitted;
        scene.Surface.CommitRequested += _onCommitRequested;
        scene.Surface.Destroyed += _onSurfaceDestroyed;
        scene.Destroyed += Dispose;
        _timeline.Easing = EasingCurve.CubicBezier(0, 0, 0.2, 1);
        _wasMapped = scene.Surface.IsMapped;
        _hadSlide = manager.SlideOf(scene.Surface) is not null;
        if (_wasMapped && _hadSlide)
        {
            BeginIn();
        }
    }

    public long DurationNanos { get; set; } = DefaultDurationNanos;

    public bool IsAnimating => _inRunning || _outRunning;

    public event Action? Started;

    internal (double X, double Y) Applied { get; private set; }

    internal bool HoldsBuffer => _snapshot is { IsDestroyed: false, NodeCount: > 0 };

    public bool Step(in FrameTick tick)
    {
        if (_inRunning)
        {
            if (_inNode is not { IsDestroyed: false } node)
            {
                _inRunning = false;
            }
            else
            {
                var t = _timeline.Progress(tick);
                var x = _dx * (1 - t);
                var y = _dy * (1 - t);
                node.Matrix = RenderTransform.Translation(x, y);
                Applied = (x, y);
                if (!_timeline.Running(tick))
                {
                    _inRunning = false;
                    _stack?.Remove(NodeName);
                    _inNode = null;
                    Applied = (0, 0);
                }
            }
        }
        else if (_outRunning)
        {
            if (_outWrap is not { IsDestroyed: false } wrap)
            {
                CancelOut();
            }
            else
            {
                var t = _timeline.Progress(tick);
                var x = _dx * t;
                var y = _dy * t;
                wrap.Matrix = RenderTransform.Translation(x, y);
                Applied = (x, y);
                if (!_timeline.Running(tick))
                {
                    CancelOut();
                }
            }
        }

        return IsAnimating;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Surface.Committed -= _onCommitted;
        _scene.Surface.CommitRequested -= _onCommitRequested;
        _scene.Surface.Destroyed -= _onSurfaceDestroyed;
        _scene.Destroyed -= Dispose;
        _inRunning = false;
        if (_inNode is { IsDestroyed: false })
        {
            _stack?.Remove(NodeName);
        }

        _inNode = null;
        CancelOut();
    }

    private void OnCommitRequested()
    {
        if (_disposed || !_wasMapped || _outPrepared)
        {
            return;
        }

        var pending = _scene.Surface.Pending;
        var unmapping = (pending.Committed & SurfaceStateFields.Buffer) != 0 && pending.Buffer is null;
        if (!unmapping || _manager.SlideOf(_scene.Surface) is null)
        {
            return;
        }

        if (_scene.Tree.Parent is not { IsDestroyed: false } parent)
        {
            return;
        }

        _outPrepared = true;
        (_outX, _outY) = _scene.Tree.ScenePosition;
        _outWidth = _scene.Surface.Current.Width;
        _outHeight = _scene.Surface.Current.Height;
        _outWrap = new SceneTransform(parent);
        _snapshot = SceneSnapshot.Capture(_scene.Tree, _outWrap);
    }

    private void OnCommitted()
    {
        if (_disposed)
        {
            return;
        }

        var mapped = _scene.Surface.IsMapped;
        var hasSlide = _manager.SlideOf(_scene.Surface) is not null;
        if (mapped && (!_wasMapped || (hasSlide && !_hadSlide)))
        {
            DropPrepared();
            if (hasSlide)
            {
                BeginIn();
            }
        }
        else if (!mapped && _wasMapped && _outPrepared)
        {
            BeginOut();
        }
        else if (_outPrepared)
        {
            DropPrepared();
        }

        _wasMapped = mapped;
        _hadSlide = hasSlide;
    }

    private void OnSurfaceDestroyed()
    {
        CancelOut();
        Dispose();
    }

    private void BeginIn()
    {
        CancelOut();
        var (x, y) = _scene.Tree.ScenePosition;
        if (!ComputeTravel(x, y, _scene.Surface.Current.Width, _scene.Surface.Current.Height, out var dx, out var dy))
        {
            return;
        }

        _dx = dx;
        _dy = dy;
        _stack ??= new TransformStack(_scene.Tree);
        _inNode = _stack.Get(NodeName) ?? _stack.Add(TransformStack.ZOrder.Transform2D, NodeName);
        _inNode.Matrix = RenderTransform.Translation(dx, dy);
        Applied = (dx, dy);
        _inRunning = true;
        _timeline.Start(DurationNanos);
        Started?.Invoke();
    }

    private void BeginOut()
    {
        _outPrepared = false;
        if (_outWrap is not { IsDestroyed: false } ||
            !ComputeTravel(_outX, _outY, _outWidth, _outHeight, out var dx, out var dy))
        {
            CancelOut();
            return;
        }

        _dx = dx;
        _dy = dy;
        Applied = (0, 0);
        _outRunning = true;
        _timeline.Start(DurationNanos);
        Started?.Invoke();
    }

    private bool ComputeTravel(int x, int y, int width, int height, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;
        if (_manager.SlideOf(_scene.Surface) is not { } slide)
        {
            return false;
        }

        var screen = _screenArea(_scene.Surface);
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return false;
        }

        switch (slide.Location)
        {
            case SlideLocation.Left:
                dx = screen.X - slide.Offset - (x + width);
                break;
            case SlideLocation.Right:
                dx = screen.X + screen.Width + slide.Offset - x;
                break;
            case SlideLocation.Top:
                dy = screen.Y - slide.Offset - (y + height);
                break;
            case SlideLocation.Bottom:
                dy = screen.Y + screen.Height + slide.Offset - y;
                break;
        }

        return dx != 0 || dy != 0;
    }

    private void DropPrepared()
    {
        _outPrepared = false;
        if (!_outRunning)
        {
            CancelOut();
        }
    }

    private void CancelOut()
    {
        _outRunning = false;
        _outPrepared = false;
        _snapshot?.Destroy();
        _snapshot = null;
        if (_outWrap is { IsDestroyed: false } wrap)
        {
            wrap.Destroy();
        }

        _outWrap = null;
    }
}
