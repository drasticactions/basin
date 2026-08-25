using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class ZoomStage : IPostStage
{
    private const int PushThreshold = 4;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;
    private ZoomOptions _options;
    private double _zoom;
    private double _target;
    private double _source;
    private double _translationX;
    private double _translationY;
    private double _cursorX;
    private double _cursorY;
    private double _prevX;
    private double _prevY;
    private long _lastNanos;
    private long _lastCursorNanos;
    private bool _hasLast;

    public ZoomStage(IPixelShader? shader, ZoomOptions options = default)
    {
        _shader = shader;
        _options = options == default ? new ZoomOptions() : options;
        _zoom = Math.Max(1.0, _options.InitialZoom);
        _target = _zoom;
        _source = _zoom;
    }

    public ZoomOptions Options
    {
        get => _options;
        set => _options = value;
    }

    public IZoomTarget? Target { get; set; }

    public double Zoom => _zoom;

    public double TargetZoom => _target;

    public double TranslationX => _translationX;

    public double TranslationY => _translationY;

    public bool IsActive => _zoom != 1.0 || _zoom != _target;

    public bool DrawsPixelGrid => _zoom >= _options.PixelGridZoom;

    public void SetCursor(double x, double y, long nowNanos)
    {
        _thread.Assert();
        _cursorX = x;
        _cursorY = y;
        _lastCursorNanos = nowNanos;
    }

    public void ZoomIn()
    {
        _thread.Assert();
        _source = _zoom;
        ZoomTo(_target * _options.ZoomFactor);
        if (_options.MouseTracking == ZoomTracking.Disabled)
        {
            _prevX = _cursorX;
            _prevY = _cursorY;
        }
    }

    public void ZoomOut()
    {
        _thread.Assert();
        _source = _zoom;
        var next = _target / _options.ZoomFactor;
        ZoomTo(next < 1.01 ? 1.0 : next);
        if (_options.MouseTracking == ZoomTracking.Disabled)
        {
            _prevX = _cursorX;
            _prevY = _cursorY;
        }
    }

    public void ZoomTo(double target)
    {
        _thread.Assert();
        _target = Math.Max(1.0, target);
    }

    public void Reset()
    {
        _thread.Assert();
        _source = _zoom;
        _target = 1.0;
    }

    public void MoveBy(double dx, double dy)
    {
        _thread.Assert();
        _prevX += dx;
        _prevY += dy;
        _cursorX += dx;
        _cursorY += dy;
    }

    public void MoveLeft(int width) => MoveBy(-width / _options.MoveFactor, 0);

    public void MoveRight(int width) => MoveBy(width / _options.MoveFactor, 0);

    public void MoveUp(int height) => MoveBy(0, -height / _options.MoveFactor);

    public void MoveDown(int height) => MoveBy(0, height / _options.MoveFactor);

    public bool Step(in FrameTick tick, int screenWidth, int screenHeight)
    {
        _thread.Assert();
        if (!_hasLast)
        {
            _lastNanos = tick.TargetPresentNanos;
            _hasLast = true;
        }

        var elapsed = (tick.TargetPresentNanos - _lastNanos) / 1_000_000.0;
        _lastNanos = tick.TargetPresentNanos;
        if (_zoom != _target && elapsed > 0)
        {
            var distance = Math.Abs(_target - _source);
            var duration = Math.Max(1.0, 150 * _options.ZoomFactor);
            _zoom = _target > _zoom
                ? Math.Min(_zoom + (distance * elapsed / duration), _target)
                : Math.Max(_zoom - (distance * elapsed / duration), _target);
        }

        var trackX = _cursorX;
        var trackY = _cursorY;
        if ((_options.FocusTracking || _options.TextCaretTracking) &&
            Target is { } target &&
            target.TryGetFocus(out var focus, out var reportedAt) &&
            !focus.IsEmpty)
        {
            var accept = _options.MouseTracking == ZoomTracking.Disabled ||
                _options.FocusDelayMillis <= 0 ||
                (reportedAt - _lastCursorNanos) / 1_000_000.0 > _options.FocusDelayMillis;
            if (accept)
            {
                trackX = focus.X + (focus.Width / 2.0);
                trackY = focus.Y + (focus.Height / 2.0);
                if (_options.MouseTracking == ZoomTracking.Disabled)
                {
                    _prevX = trackX;
                    _prevY = trackY;
                }
            }
        }

        Track(trackX, trackY, screenWidth, screenHeight);
        return IsActive;
    }

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(frame);
        _thread.Assert();
        var full = new Box(0, 0, context.Width, context.Height);
        if (_zoom <= 1.0)
        {
            pass.AddTexture(frame, new TextureRenderOptions { DstBox = full });
            return;
        }

        if (_shader is { } shader)
        {
            var grid = DrawsPixelGrid ? 1f : 0f;
            shader.SetUniforms([(float)_zoom, grid, ((float)_translationX, (float)_translationY)]);
            pass.AddTexture(frame, new TextureRenderOptions { DstBox = full, Shader = shader });
            return;
        }

        pass.AddTexture(frame, new TextureRenderOptions
        {
            DstBox = full,
            Transform = RenderTransform.Multiply(
                RenderTransform.Translation(_translationX, _translationY),
                RenderTransform.Scale(_zoom, _zoom)),
        });
    }

    private void Track(double trackX, double trackY, int width, int height)
    {
        switch (_options.MouseTracking)
        {
            case ZoomTracking.Proportional:
                _translationX = -(int)(trackX * (_zoom - 1.0));
                _translationY = -(int)(trackY * (_zoom - 1.0));
                _prevX = _cursorX;
                _prevY = _cursorY;
                break;

            case ZoomTracking.Centered:
                _prevX = _cursorX;
                _prevY = _cursorY;
                _translationX = Math.Min(0, Math.Max((int)(width - (width * _zoom)), (int)((width / 2.0) - (trackX * _zoom))));
                _translationY = Math.Min(0, Math.Max((int)(height - (height * _zoom)), (int)((height / 2.0) - (trackY * _zoom))));
                break;

            case ZoomTracking.CenteredStrict:
                _prevX = _cursorX;
                _prevY = _cursorY;
                _translationX = (int)((width / 2.0) - (trackX * _zoom));
                _translationY = (int)((height / 2.0) - (trackY * _zoom));
                break;

            case ZoomTracking.Disabled:
                _translationX = Math.Min(0, Math.Max((int)(width - (width * _zoom)), (int)((width / 2.0) - (_prevX * _zoom))));
                _translationY = Math.Min(0, Math.Max((int)(height - (height * _zoom)), (int)((height / 2.0) - (_prevY * _zoom))));
                break;

            default:
            {
                var x = (trackX * _zoom) - (_prevX * (_zoom - 1.0));
                var y = (trackY * _zoom) - (_prevY * (_zoom - 1.0));
                double moveX = 0, moveY = 0;
                if (x < PushThreshold)
                {
                    moveX = (x - PushThreshold) / _zoom;
                }
                else if (x > width - PushThreshold)
                {
                    moveX = (x + PushThreshold - width) / _zoom;
                }

                if (y < PushThreshold)
                {
                    moveY = (y - PushThreshold) / _zoom;
                }
                else if (y > height - PushThreshold)
                {
                    moveY = (y + PushThreshold - height) / _zoom;
                }

                _prevX += moveX;
                _prevY += moveY;
                _translationX = -(int)(_prevX * (_zoom - 1.0));
                _translationY = -(int)(_prevY * (_zoom - 1.0));
                break;
            }
        }
    }
}
