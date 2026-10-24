using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the screen transform effect as a post stage. It turns the new frame into place over a fading copy of the old one when the output transform changes, and it can also shake the screen.
/// </summary>
public sealed class ScreenTransformAnimation : IPostStage, IDisposable
{
    /// <summary>
    /// The default duration, in milliseconds.
    /// </summary>
    public const double DefaultMillis = 250;

    /// <summary>
    /// The number of full oscillations in a shake.
    /// </summary>
    public const int ShakeCycles = 3;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IRenderer _renderer;
    private EffectTimeline _timeline;
    private BufferLock _hold;
    private ITexture? _texture;
    private double _angle;
    private double _shakeX;
    private double _shakeY;
    private bool _shaking;
    private bool _running;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScreenTransformAnimation"/> class.
    /// </summary>
    /// <param name="renderer">The renderer that imports the held buffer of the old frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public ScreenTransformAnimation(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether a rotation or a shake runs.
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Gets the eased progress of the current animation, from 0 to 1. It is 1 after an animation ends.
    /// </summary>
    public double Progress { get; private set; }

    /// <summary>
    /// Gets the rotation from the old transform to the new one, in degrees from −180 to 180. It is zero for a shake.
    /// </summary>
    public double Angle => _angle;

    /// <summary>
    /// Gets a value indicating whether a shake runs.
    /// </summary>
    public bool IsShaking => _running && _shaking;

    /// <summary>
    /// Gets the current shake offset, in pixels. It is a sine of <see cref="ShakeCycles"/> cycles that decays to zero, and zero when no shake runs.
    /// </summary>
    public (double X, double Y) ShakeOffset
    {
        get
        {
            if (!IsShaking)
            {
                return (0, 0);
            }

            var wave = (1.0 - Progress) * Math.Sin(Progress * Math.PI * 2 * ShakeCycles);
            return (_shakeX * wave, _shakeY * wave);
        }
    }

    /// <summary>
    /// Calculates the shortest rotation between the rotation parts of two output transforms. A flip is ignored.
    /// </summary>
    /// <param name="from">The old transform.</param>
    /// <param name="to">The new transform.</param>
    /// <returns>The rotation, in degrees from −180 to 180.</returns>
    public static double AngleBetween(OutputTransform from, OutputTransform to)
    {
        var degrees = (((int)to % 4) - ((int)from % 4)) * 90;
        return degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
    }

    /// <summary>
    /// Starts a rotation and locks the buffer of the old frame until the end. It replaces a running animation.
    /// </summary>
    /// <param name="previous">The last frame before the change. The animation locks it.</param>
    /// <param name="from">The old output transform.</param>
    /// <param name="to">The new output transform.</param>
    /// <param name="now">The tick that the animation starts at.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled, and then nothing starts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="previous"/> is <see langword="null"/>.</exception>
    public bool Begin(IBuffer previous, OutputTransform from, OutputTransform to, in FrameTick now, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(previous);
        _thread.Assert();
        if (duration.IsDisabled)
        {
            return false;
        }

        Release();
        _shaking = false;
        _hold = previous.Lock();
        _angle = AngleBetween(from, to);
        _timeline.Easing = EasingCurve.InOutCubic;
        _timeline.Start(duration.Nanos);
        _timeline.Anchor(now);
        Progress = 0;
        _running = true;
        return true;
    }

    /// <summary>
    /// Starts a shake. A running shake restarts, and a running rotation is not interrupted.
    /// </summary>
    /// <param name="amplitudeX">The horizontal amplitude, in pixels.</param>
    /// <param name="amplitudeY">The vertical amplitude, in pixels.</param>
    /// <param name="now">The tick that the shake starts at.</param>
    /// <param name="duration">The length of the shake.</param>
    /// <returns><see langword="false"/> when the duration is disabled, a rotation runs, or both amplitudes are zero.</returns>
    public bool BeginShake(double amplitudeX, double amplitudeY, in FrameTick now, AnimationDuration duration)
    {
        _thread.Assert();
        if (duration.IsDisabled || (_running && !_shaking) || (amplitudeX == 0 && amplitudeY == 0))
        {
            return false;
        }

        _shakeX = amplitudeX;
        _shakeY = amplitudeY;
        _shaking = true;
        _angle = 0;
        _timeline.Easing = EasingCurve.Linear;
        _timeline.Start(duration.Nanos);
        _timeline.Anchor(now);
        Progress = 0;
        _running = true;
        return true;
    }

    /// <summary>
    /// Updates <see cref="Progress"/> for a tick. At the end it releases the old frame.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the animation runs.</returns>
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
    /// Draws the frame unchanged, shaken, or rotated under the fading old frame, on a black background.
    /// </summary>
    /// <param name="pass">The render pass of the output.</param>
    /// <param name="frame">The texture of the composited frame.</param>
    /// <param name="context">The output size and damage.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pass"/> or <paramref name="frame"/> is <see langword="null"/>.</exception>
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

        if (_shaking)
        {
            var (offsetX, offsetY) = ShakeOffset;
            pass.AddRect(new RenderColor(0f, 0f, 0f, 1f), full);
            pass.AddTexture(frame, new TextureRenderOptions
            {
                DstBox = full,
                Transform = RenderTransform.Translation(offsetX, offsetY),
            });
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

    /// <summary>
    /// Releases the old frame and its texture and stops the animation. A second call does nothing.
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
