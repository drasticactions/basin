using Basin.Capabilities;

namespace Basin.Backend.Wayland;

/// <summary>
/// Base class for chrome that draws a <see cref="WaylandHostFrame"/> through an <see cref="IUIHost"/>.
/// It tracks the frame's state and pointer events and owns the UI surface. The subclass owns the insets, the hit test and the actions.
/// </summary>
public abstract class HostFrameChrome : IDisposable
{
    private int _shownWidth;
    private int _shownHeight;
    private double _shownScale;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostFrameChrome"/> class.
    /// It subscribes to the state and pointer events of <paramref name="frame"/> until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="frame">The host frame to draw. The caller keeps ownership.</param>
    /// <param name="host">The UI host that creates the chrome surface. The caller keeps ownership.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="host"/> is <see langword="null"/>.</exception>
    protected HostFrameChrome(WaylandHostFrame frame, IUIHost host)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(host);
        Frame = frame;
        Host = host;
        Frame.StateChanged += HandleStateChanged;
        Frame.PointerEnter += HandlePointerEnter;
        Frame.PointerMotion += HandlePointerMotion;
        Frame.PointerButton += HandlePointerButton;
        Frame.PointerLeave += HandlePointerLeave;
    }

    /// <summary>Gets the host frame that this chrome draws.</summary>
    protected WaylandHostFrame Frame { get; }

    /// <summary>Gets the UI host that creates the chrome surface.</summary>
    protected IUIHost Host { get; }

    /// <summary>
    /// Gets the UI surface that <see cref="EnsureSurface"/> created. It is <see langword="null"/> before the first successful call and after <see cref="Dispose"/>.
    /// </summary>
    protected IUISurface? Surface { get; private set; }

    /// <summary>Gets the last pointer X position over the frame, in logical pixels from the left of the outer box.</summary>
    protected double PointerX { get; private set; }

    /// <summary>Gets the last pointer Y position over the frame, in logical pixels from the top of the outer box.</summary>
    protected double PointerY { get; private set; }

    /// <summary>Gets a value indicating whether the pointer is over one of the frame bands now.</summary>
    protected bool PointerInside { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="Dispose"/> ran.</summary>
    protected bool IsDisposed { get; private set; }

    /// <summary>
    /// Lays out the chrome again when the outer size or the scale changed since the last surface. Subscribe it to the output's committed event.
    /// </summary>
    /// <param name="fields">The fields that the output commit changed. The method does not read them.</param>
    public void OnOutputChanged(OutputStateFields fields)
    {
        if (Surface is not null && Frame.OuterWidth == _shownWidth && Frame.OuterHeight == _shownHeight && Frame.Scale == _shownScale)
        {
            return;
        }

        Relayout();
    }

    /// <summary>
    /// Unsubscribes from the frame and disposes the UI surface. The frame and the host stay alive. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        Frame.StateChanged -= HandleStateChanged;
        Frame.PointerEnter -= HandlePointerEnter;
        Frame.PointerMotion -= HandlePointerMotion;
        Frame.PointerButton -= HandlePointerButton;
        Frame.PointerLeave -= HandlePointerLeave;
        if (Surface is { } surface)
        {
            OnDisposingSurface(surface);
            surface.Dispose();
            Surface = null;
        }
    }

    /// <summary>
    /// Sets the frame insets and draws the chrome again. The base class calls it when the frame state, the outer size or the scale changes.
    /// </summary>
    protected abstract void Relayout();

    /// <summary>
    /// Runs when the frame state changes, for example on activation, maximize or a new title. The default calls <see cref="Relayout"/>.
    /// </summary>
    protected virtual void OnStateChanged() => Relayout();

    /// <summary>Runs when the pointer enters a frame band, after <see cref="PointerX"/> and <see cref="PointerY"/> are updated.</summary>
    /// <param name="x">The X position in logical pixels from the left of the outer box.</param>
    /// <param name="y">The Y position in logical pixels from the top of the outer box.</param>
    protected virtual void OnPointerEnter(double x, double y)
    {
    }

    /// <summary>Runs when the pointer moves over a frame band, after <see cref="PointerX"/> and <see cref="PointerY"/> are updated.</summary>
    /// <param name="timeMs">The parent's event time in milliseconds.</param>
    /// <param name="x">The X position in logical pixels from the left of the outer box.</param>
    /// <param name="y">The Y position in logical pixels from the top of the outer box.</param>
    protected virtual void OnPointerMotion(uint timeMs, double x, double y)
    {
    }

    /// <summary>Runs when a pointer button changes state over a frame band.</summary>
    /// <param name="timeMs">The parent's event time in milliseconds.</param>
    /// <param name="button">The Linux input event code of the button, for example <see cref="InputCodes.BtnLeft"/>.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    protected virtual void OnPointerButton(uint timeMs, uint button, bool pressed)
    {
    }

    /// <summary>Runs when the pointer leaves the frame bands, after <see cref="PointerInside"/> becomes <see langword="false"/>.</summary>
    protected virtual void OnPointerLeave()
    {
    }

    /// <summary>Runs once after <see cref="EnsureSurface"/> creates the UI surface.</summary>
    /// <param name="surface">The new surface, also available as <see cref="Surface"/>.</param>
    protected virtual void OnSurfaceCreated(IUISurface surface)
    {
    }

    /// <summary>Runs during <see cref="Dispose"/>, before the UI surface is disposed.</summary>
    /// <param name="surface">The surface that will be disposed.</param>
    protected virtual void OnDisposingSurface(IUISurface surface)
    {
    }

    /// <summary>
    /// Creates the UI surface at the frame's outer size and scale, or configures the existing one to them. It asks for a dmabuf target when the host can produce one.
    /// </summary>
    /// <returns><see langword="false"/> when the outer size is empty, or the host cannot create or configure the surface.</returns>
    protected bool EnsureSurface()
    {
        var width = Frame.OuterWidth;
        var height = Frame.OuterHeight;
        var scale = Frame.Scale;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (Surface is null)
        {
            var target = (Host.Produces & UITargetKind.Dmabuf) != 0 ? UITargetKind.Dmabuf : UITargetKind.Memory;
            Surface = Host.CreateSurface(new UISurfaceOptions { Target = target, Width = width, Height = height, Scale = scale });
            if (Surface is null)
            {
                return false;
            }

            OnSurfaceCreated(Surface);
        }
        else if (!Surface.Configure(width, height, scale))
        {
            return false;
        }

        _shownWidth = width;
        _shownHeight = height;
        _shownScale = scale;
        return true;
    }

    /// <summary>
    /// Attaches the frame that the UI surface produced last to the host frame, with its damage.
    /// </summary>
    /// <returns><see langword="false"/> when the chrome is disposed, no frame is ready, or the host frame refuses the buffer.</returns>
    protected bool Present()
    {
        if (IsDisposed || Surface is not { } surface || !surface.TryAcquire(out var produced))
        {
            return false;
        }

        using (produced)
        {
            return produced.Buffer is { } buffer && Frame.Attach(buffer, produced.Damage);
        }
    }

    /// <summary>
    /// Clears <see cref="Surface"/> without disposing it, when it is <paramref name="surface"/>. Use it when the subclass disposed the surface itself.
    /// </summary>
    /// <param name="surface">The surface to forget.</param>
    protected void ForgetSurface(IUISurface surface)
    {
        if (ReferenceEquals(surface, Surface))
        {
            Surface = null;
        }
    }

    private void HandleStateChanged() => OnStateChanged();

    private void HandlePointerEnter(double x, double y)
    {
        PointerX = x;
        PointerY = y;
        PointerInside = true;
        OnPointerEnter(x, y);
    }

    private void HandlePointerMotion(uint timeMs, double x, double y)
    {
        PointerX = x;
        PointerY = y;
        OnPointerMotion(timeMs, x, y);
    }

    private void HandlePointerButton(uint timeMs, uint button, bool pressed) => OnPointerButton(timeMs, button, pressed);

    private void HandlePointerLeave()
    {
        PointerInside = false;
        OnPointerLeave();
    }
}
