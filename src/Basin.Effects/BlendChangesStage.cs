using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class BlendChangesStage : IPostStage, IDisposable
{
    public const double DefaultMillis = 400;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IRenderer _renderer;
    private EffectTimeline _timeline;
    private BufferLock _hold;
    private ITexture? _texture;
    private bool _running;
    private bool _disposed;

    public BlendChangesStage(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        BasinCounters.Track();
    }

    public bool IsRunning => _running;

    public double Progress { get; private set; }

    public bool Begin(IBuffer previous, in FrameTick now, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(previous);
        _thread.Assert();
        if (duration.IsDisabled)
        {
            return false;
        }

        Release();
        _hold = previous.Lock();
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
        pass.AddTexture(frame, new TextureRenderOptions { DstBox = full });
        if (!_running || _hold.Buffer is not { } previous)
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
            Alpha = (float)Math.Clamp(1.0 - Progress, 0, 1),
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
