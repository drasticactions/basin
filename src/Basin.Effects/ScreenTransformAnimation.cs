using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class ScreenTransformAnimation : IPostStage, IDisposable
{
    public const double DefaultMillis = 250;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IRenderer _renderer;
    private EffectTimeline _timeline;
    private BufferLock _hold;
    private ITexture? _texture;
    private double _angle;
    private bool _running;
    private bool _disposed;

    public ScreenTransformAnimation(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        BasinCounters.Track();
    }

    public bool IsRunning => _running;

    public double Progress { get; private set; }

    public double Angle => _angle;

    public static double AngleBetween(OutputTransform from, OutputTransform to)
    {
        var degrees = (((int)to % 4) - ((int)from % 4)) * 90;
        return degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
    }

    public bool Begin(IBuffer previous, OutputTransform from, OutputTransform to, in FrameTick now, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(previous);
        _thread.Assert();
        if (duration.IsDisabled)
        {
            return false;
        }

        Release();
        _hold = previous.Lock();
        _angle = AngleBetween(from, to);
        _timeline.Easing = EasingCurve.InOutCubic;
        _timeline.Start(duration.Nanos);
        _timeline.Anchor(now);
        Progress = 0;
        _running = true;
        return true;
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_running)
        {
            return false;
        }

        Progress = _timeline.Progress(tick);
        if (_timeline.Running(tick))
        {
            return true;
        }

        Release();
        _running = false;
        Progress = 1;
        return false;
    }

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(frame);
        _thread.Assert();
        var full = new Box(0, 0, context.Width, context.Height);
        if (!_running)
        {
            pass.AddTexture(frame, new TextureRenderOptions { DstBox = full });
            return;
        }

        var remaining = 1.0 - Progress;
        var rotation = RenderTransform.RotationAbout(
            _angle * remaining * Math.PI / 180.0, context.Width / 2.0, context.Height / 2.0);
        pass.AddRect(new RenderColor(0f, 0f, 0f, 1f), full);
        pass.AddTexture(frame, new TextureRenderOptions { DstBox = full, Transform = rotation });

        if (_hold.Buffer is not { } previous)
        {
            return;
        }

        _texture ??= _renderer.ImportTexture(previous);
        if (_texture is null)
        {
            return;
        }

        (_texture as IRefreshableTexture)?.MarkDirty();
        pass.AddTexture(_texture, new TextureRenderOptions
        {
            DstBox = full,
            Transform = rotation,
            Alpha = (float)Math.Clamp(remaining, 0, 1),
        });
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        Release();
        _running = false;
    }

    private void Release()
    {
        _texture?.Dispose();
        _texture = null;
        _hold.Dispose();
        _hold = default;
    }
}
