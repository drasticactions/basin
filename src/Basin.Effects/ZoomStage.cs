using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the full-screen zoom as a post stage. It scales the whole frame and moves the view with the cursor or a focus rectangle.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomStage"/> class.
    /// </summary>
    /// <param name="shader">The compiled <see cref="ZoomShader"/>, or <see langword="null"/>. Without it, the stage scales through a render transform with no sharpening and no pixel grid.</param>
    /// <param name="options">The settings. The default value uses the default <see cref="ZoomOptions"/>.</param>
    public ZoomStage(IPixelShader? shader, ZoomOptions options = default)
    {
        _shader = shader;
        _options = options == default ? new ZoomOptions() : options;
        _zoom = Math.Max(1.0, _options.InitialZoom);
        _target = _zoom;
        _source = _zoom;
    }

    /// <summary>
    /// Gets or sets the settings. A new value applies on the next step.
    /// </summary>
    public ZoomOptions Options
    {
        get => _options;
        set => _options = value;
    }

    /// <summary>
    /// Gets or sets the source of focus and caret rectangles, or <see langword="null"/> for none.
    /// </summary>
    public IZoomTarget? Target { get; set; }

    /// <summary>
    /// Gets the current zoom. A value of 1 draws the frame unchanged.
    /// </summary>
    public double Zoom => _zoom;

    /// <summary>
    /// Gets the zoom that <see cref="Step"/> moves toward.
    /// </summary>
    public double TargetZoom => _target;

    /// <summary>
    /// Gets the horizontal translation of the scaled frame, in output pixels.
    /// </summary>
    public double TranslationX => _translationX;

    /// <summary>
    /// Gets the vertical translation of the scaled frame, in output pixels.
    /// </summary>
    public double TranslationY => _translationY;

    /// <summary>
    /// Gets a value indicating whether the frame is zoomed or the zoom still moves.
    /// </summary>
    public bool IsActive => _zoom != 1.0 || _zoom != _target;

    /// <summary>
    /// Gets a value indicating whether the zoom is at or above <see cref="ZoomOptions.PixelGridZoom"/>.
    /// </summary>
    public bool DrawsPixelGrid => _zoom >= _options.PixelGridZoom;

    /// <summary>
    /// Records the cursor position and the time of the motion. The focus delay counts from this time.
    /// </summary>
    /// <param name="x">The horizontal position, in output pixels.</param>
    /// <param name="y">The vertical position, in output pixels.</param>
    /// <param name="nowNanos">The time of the motion, in nanoseconds.</param>
    public void SetCursor(double x, double y, long nowNanos)
    {
        _thread.Assert();
        _cursorX = x;
        _cursorY = y;
        _lastCursorNanos = nowNanos;
    }

    /// <summary>
    /// Multiplies the target zoom by <see cref="ZoomOptions.ZoomFactor"/>.
    /// </summary>
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

    /// <summary>
    /// Divides the target zoom by <see cref="ZoomOptions.ZoomFactor"/>. A result below 1.01 becomes 1.
    /// </summary>
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

    /// <summary>
    /// Sets the target zoom without a new start for the animation.
    /// </summary>
    /// <param name="target">The target zoom. Values below 1 use 1.</param>
    public void ZoomTo(double target)
    {
        _thread.Assert();
        _target = Math.Max(1.0, target);
    }

    /// <summary>
    /// Sets the target zoom to 1.
    /// </summary>
    public void Reset()
    {
        _thread.Assert();
        _source = _zoom;
        _target = 1.0;
    }

    /// <summary>
    /// Pans the tracked point by an offset.
    /// </summary>
    /// <param name="dx">The horizontal offset, in output pixels.</param>
    /// <param name="dy">The vertical offset, in output pixels.</param>
    public void MoveBy(double dx, double dy)
    {
        _thread.Assert();
        _prevX += dx;
        _prevY += dy;
        _cursorX += dx;
        _cursorY += dy;
    }

    /// <summary>
    /// Pans left by the width over <see cref="ZoomOptions.MoveFactor"/>.
    /// </summary>
    /// <param name="width">The screen width, in pixels.</param>
    public void MoveLeft(int width) => MoveBy(-width / _options.MoveFactor, 0);

    /// <summary>
    /// Pans right by the width over <see cref="ZoomOptions.MoveFactor"/>.
    /// </summary>
    /// <param name="width">The screen width, in pixels.</param>
    public void MoveRight(int width) => MoveBy(width / _options.MoveFactor, 0);

    /// <summary>
    /// Pans up by the height over <see cref="ZoomOptions.MoveFactor"/>.
    /// </summary>
    /// <param name="height">The screen height, in pixels.</param>
    public void MoveUp(int height) => MoveBy(0, -height / _options.MoveFactor);

    /// <summary>
    /// Pans down by the height over <see cref="ZoomOptions.MoveFactor"/>.
    /// </summary>
    /// <param name="height">The screen height, in pixels.</param>
    public void MoveDown(int height) => MoveBy(0, height / _options.MoveFactor);

    /// <summary>
    /// Moves the zoom linearly toward the target and updates the translation from the cursor or an accepted focus rectangle.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <param name="screenWidth">The output width, in pixels.</param>
    /// <param name="screenHeight">The output height, in pixels.</param>
    /// <returns>The value of <see cref="IsActive"/> after the step.</returns>
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
        if (_options.FocusTracking &&
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

    /// <summary>
    /// Maps a box through the current zoom and translation, rounded outward.
    /// </summary>
    /// <param name="scene">The box in the composited frame, in physical pixels.</param>
    /// <param name="context">The frame size, timing and damage.</param>
    /// <param name="output">The box on the output.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool TryMapBox(in Box scene, in PostContext context, out Box output)
    {
        if (_zoom <= 1.0)
        {
            output = scene;
            return true;
        }

        output = new Box(
            (int)Math.Floor((scene.X * _zoom) + _translationX),
            (int)Math.Floor((scene.Y * _zoom) + _translationY),
            (int)Math.Ceiling(scene.Width * _zoom),
            (int)Math.Ceiling(scene.Height * _zoom));
        return true;
    }

    /// <inheritdoc/>
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
