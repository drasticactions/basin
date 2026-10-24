using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's magnifier as a post stage. It draws an enlarged lens of the frame around a center point, and ramps its zoom toward a target.
/// </summary>
public sealed class MagnifierStage : IPostStage
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly MagnifierOptions _options;
    private double _zoom;
    private double _target;
    private long _lastNanos;
    private bool _hasLast;

    /// <summary>
    /// Initializes a new instance of the <see cref="MagnifierStage"/> class.
    /// </summary>
    /// <param name="options">The settings. The default value uses the default <see cref="MagnifierOptions"/>.</param>
    public MagnifierStage(MagnifierOptions options = default)
    {
        _options = options == default ? new MagnifierOptions() : options;
        _zoom = Math.Max(1.0, _options.InitialZoom);
        _target = _zoom;
    }

    /// <summary>
    /// Gets the settings of the stage.
    /// </summary>
    public MagnifierOptions Options => _options;

    /// <summary>
    /// Gets the current zoom. A value of 1 draws no lens.
    /// </summary>
    public double Zoom => _zoom;

    /// <summary>
    /// Gets or sets the zoom that <see cref="Step"/> ramps toward. Values below 1 use 1.
    /// </summary>
    public double TargetZoom
    {
        get => _target;
        set => _target = Math.Max(1.0, value);
    }

    /// <summary>
    /// Gets or sets the horizontal center of the lens, in output pixels.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Gets or sets the vertical center of the lens, in output pixels.
    /// </summary>
    public double CenterY { get; set; }

    /// <summary>
    /// Gets a value indicating whether the lens is visible or the zoom still ramps.
    /// </summary>
    public bool IsActive => _zoom != 1.0 || _zoom != _target;

    /// <summary>
    /// Multiplies the target zoom by <see cref="MagnifierOptions.ZoomFactor"/>. A step during a ramp compounds on the target.
    /// </summary>
    public void ZoomIn()
    {
        _thread.Assert();
        TargetZoom = _target * _options.ZoomFactor;
    }

    /// <summary>
    /// Divides the target zoom by <see cref="MagnifierOptions.ZoomFactor"/>, down to 1.
    /// </summary>
    public void ZoomOut()
    {
        _thread.Assert();
        TargetZoom = _target / _options.ZoomFactor;
    }

    /// <summary>
    /// Sets the target zoom to 1, so the lens ramps away.
    /// </summary>
    public void Reset()
    {
        _thread.Assert();
        TargetZoom = 1.0;
    }

    /// <summary>
    /// Ramps the zoom toward the target by the time since the last tick, multiplicatively and by at least 20 percent per tick.
    /// </summary>
    /// <param name="tick">The frame tick. The first tick only starts the clock.</param>
    /// <returns>The value of <see cref="IsActive"/> after the step.</returns>
    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_hasLast)
        {
            _lastNanos = tick.TargetPresentNanos;
            _hasLast = true;
            return IsActive;
        }

        var elapsed = (tick.TargetPresentNanos - _lastNanos) / 1_000_000.0;
        _lastNanos = tick.TargetPresentNanos;
        if (elapsed > 0 && _zoom != _target)
        {
            var diff = elapsed / Math.Max(1.0, _options.RampMillis);
            _zoom = _target > _zoom
                ? Math.Min(_zoom * Math.Max(1 + diff, 1.2), _target)
                : Math.Max(_zoom * Math.Min(1 - diff, 0.8), _target);
        }

        return IsActive;
    }

    /// <summary>
    /// Reports every box at its own place. The lens content is not mapped.
    /// </summary>
    /// <param name="scene">The box in the composited frame, in physical pixels.</param>
    /// <param name="context">The frame size, timing and damage.</param>
    /// <param name="output">The same box as <paramref name="scene"/>.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool TryMapBox(in Box scene, in PostContext context, out Box output)
    {
        output = scene;
        return true;
    }

    /// <inheritdoc/>
    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(frame);
        _thread.Assert();
        var full = new Box(0, 0, context.Width, context.Height);
        pass.AddTexture(frame, new TextureRenderOptions { DstBox = full });
        if (_zoom <= 1.0)
        {
            return;
        }

        var area = new Box(
            (int)Math.Round(CenterX) - (_options.Width / 2),
            (int)Math.Round(CenterY) - (_options.Height / 2),
            _options.Width,
            _options.Height);
        var source = new FBox(
            CenterX - (_options.Width / (_zoom * 2)),
            CenterY - (_options.Height / (_zoom * 2)),
            _options.Width / _zoom,
            _options.Height / _zoom);

        pass.AddTexture(frame, new TextureRenderOptions { DstBox = area, SrcBox = source });

        var border = _options.FrameWidth;
        if (border <= 0)
        {
            return;
        }

        var black = new RenderColor(0f, 0f, 0f, 1f);
        pass.AddRect(black, new Box(area.X - border, area.Y - border, area.Width + (2 * border), border));
        pass.AddRect(black, new Box(area.X - border, area.Bottom, area.Width + (2 * border), border));
        pass.AddRect(black, new Box(area.X - border, area.Y, border, area.Height));
        pass.AddRect(black, new Box(area.Right, area.Y, border, area.Height));
    }
}
