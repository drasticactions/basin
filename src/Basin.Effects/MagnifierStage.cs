using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class MagnifierStage : IPostStage
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly MagnifierOptions _options;
    private double _zoom;
    private double _target;
    private long _lastNanos;
    private bool _hasLast;

    public MagnifierStage(MagnifierOptions options = default)
    {
        _options = options == default ? new MagnifierOptions() : options;
        _zoom = Math.Max(1.0, _options.InitialZoom);
        _target = _zoom;
    }

    public MagnifierOptions Options => _options;

    public double Zoom => _zoom;

    public double TargetZoom
    {
        get => _target;
        set => _target = Math.Max(1.0, value);
    }

    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public bool IsActive => _zoom != 1.0 || _zoom != _target;

    public void ZoomIn()
    {
        _thread.Assert();
        TargetZoom = _target * _options.ZoomFactor;
    }

    public void ZoomOut()
    {
        _thread.Assert();
        TargetZoom = _target / _options.ZoomFactor;
    }

    public void Reset()
    {
        _thread.Assert();
        TargetZoom = 1.0;
    }

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
