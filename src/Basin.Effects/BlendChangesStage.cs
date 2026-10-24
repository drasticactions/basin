using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// A post stage that crossfades from a frame the consumer supplies to the live frame. The consumer captures the previous frame before the change.
/// </summary>
public sealed class BlendChangesStage : IPostStage, IDisposable
{
    /// <summary>
    /// The KWin default crossfade duration in milliseconds.
    /// </summary>
    public const double DefaultMillis = 400;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IRenderer _renderer;
    private EffectTimeline _timeline;
    private BufferLock _hold;
    private ITexture? _texture;
    private bool _running;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlendChangesStage"/> class.
    /// </summary>
    /// <param name="renderer">The renderer that imports the previous frame as a texture.</param>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public BlendChangesStage(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether a crossfade is running and <see cref="Step"/> must be called.
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Gets the eased crossfade progress, from 0 at the start to 1 at the end.
    /// </summary>
    public double Progress { get; private set; }

    /// <summary>
    /// Starts a crossfade from a buffer. The stage locks the buffer until the fade ends or a new one starts.
    /// </summary>
    /// <param name="previous">The frame to fade out of.</param>
    /// <param name="now">The tick that anchors the fade.</param>
    /// <param name="duration">The fade duration.</param>
    /// <returns><see langword="false"/> when the duration is disabled and no fade starts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="previous"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Advances the crossfade. At the end it releases the previous frame.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the fade needs more frames.</returns>
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

    /// <summary>
    /// Leaves the box where it is, because the stage draws the frame at its own position. Unlike the default, it always returns <see langword="true"/>.
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

    /// <summary>
    /// Draws the live frame, then the previous frame over it at the remaining alpha while a fade runs.
    /// </summary>
    /// <param name="pass">The pass on the output buffer.</param>
    /// <param name="frame">The texture that holds the composited scene.</param>
    /// <param name="context">The frame size, timing and damage.</param>
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

    /// <summary>
    /// Releases the previous frame and stops the fade. A second call does nothing.
    /// </summary>
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
