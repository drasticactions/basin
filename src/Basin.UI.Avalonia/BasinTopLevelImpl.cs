using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering.Composition;

namespace Basin.UI.Avalonia;

internal abstract class BasinTopLevelImpl : ITopLevelImpl, IFramebufferPlatformSurface
{
    private readonly BasinPlatformContext _context;
    private Size _clientSize = new(1, 1);
    private double _renderScaling = 1.0;
    private bool _disposed;

    protected BasinTopLevelImpl(BasinPlatformContext context)
    {
        _context = context;
        Framebuffer = new BasinFramebuffer();
        Surfaces = [this];
        MouseDevice = new MouseDevice(new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true));
        Framebuffer.Configure((int)_clientSize.Width, (int)_clientSize.Height, _renderScaling);
    }

    public BasinFramebuffer Framebuffer { get; }

    public BasinPlatformContext Context => _context;

    public AvaloniaUISurface? Surface { get; internal set; }

    public IMouseDevice MouseDevice { get; }

    public IInputRoot? InputRoot { get; private set; }

    public Size ClientSize => _clientSize;

    public Size? FrameSize => null;

    public double RenderScaling => _renderScaling;

    public double DesktopScaling => _renderScaling;

    public IPlatformRenderSurface[] Surfaces { get; }

    public IPlatformHandle? Handle => null;

    public Compositor Compositor => _context.Compositor;

    public Action<RawInputEventArgs>? Input { get; set; }

    public Action<Rect>? Paint { get; set; }

    public Action<Size, WindowResizeReason>? Resized { get; set; }

    public Action<double>? ScalingChanged { get; set; }

    public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }

    public Action? Closed { get; set; }

    public Action? LostFocus { get; set; }

    public WindowTransparencyLevel TransparencyLevel { get; private set; } = WindowTransparencyLevel.Transparent;

    public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => new(1, 1, 1);

    public PixelPoint ScreenPosition { get; internal set; }

    public bool IsDisposed => _disposed;

    public void SetInputRoot(IInputRoot inputRoot) => InputRoot = inputRoot;

    public global::Avalonia.Point PointToClient(PixelPoint point) =>
        new((point.X - ScreenPosition.X) / _renderScaling, (point.Y - ScreenPosition.Y) / _renderScaling);

    public PixelPoint PointToScreen(global::Avalonia.Point point) => new(
        ScreenPosition.X + (int)Math.Round(point.X * _renderScaling),
        ScreenPosition.Y + (int)Math.Round(point.Y * _renderScaling));

    public string? CursorName { get; private set; }

    public virtual void SetCursor(ICursorImpl? cursor) =>
        CursorName = (cursor as BasinCursor)?.Name;

    public virtual IPopupImpl? CreatePopup()
    {
        if (_disposed || _context.Screens is not { } screens)
        {
            return null;
        }

        var popup = new BasinPopupImpl(_context, this, screens);
        _ = new AvaloniaUISurface(popup, ownsRoot: false, _context.Host);
        return popup;
    }

    public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels)
    {
        foreach (var level in transparencyLevels)
        {
            if (level == WindowTransparencyLevel.Transparent || level == WindowTransparencyLevel.None)
            {
                if (TransparencyLevel != level)
                {
                    TransparencyLevel = level;
                    TransparencyLevelChanged?.Invoke(level);
                }

                return;
            }
        }
    }

    public PlatformThemeVariant? FrameThemeVariant { get; private set; }

    public void SetFrameThemeVariant(PlatformThemeVariant? themeVariant) => FrameThemeVariant = themeVariant;

    public virtual object? TryGetFeature(Type featureType) => _context.TryGetFeature(featureType);

    public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new RenderTarget(this);

    public bool Resize(int logicalWidth, int logicalHeight, double scale, WindowResizeReason reason)
    {
        if (logicalWidth <= 0 || logicalHeight <= 0 || scale <= 0)
        {
            return false;
        }

        var scaleChanged = scale != _renderScaling;
        var size = new Size(logicalWidth, logicalHeight);
        var sizeChanged = size != _clientSize;
        if (!scaleChanged && !sizeChanged)
        {
            return true;
        }

        if (!Framebuffer.Configure(logicalWidth, logicalHeight, scale))
        {
            return false;
        }

        _clientSize = size;
        _renderScaling = scale;

        if (scaleChanged)
        {
            ScalingChanged?.Invoke(scale);
        }

        if (sizeChanged || scaleChanged)
        {
            Resized?.Invoke(size, reason);
        }

        return true;
    }

    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Closed?.Invoke();
        Framebuffer.Dispose();
    }

    private void OnFramePublished() => Surface?.NotifyFramePublished();

    private sealed class RenderTarget : IFramebufferRenderTarget
    {
        private readonly BasinTopLevelImpl _impl;

        public RenderTarget(BasinTopLevelImpl impl) => _impl = impl;

        public bool RetainsFrameContents => true;

        public PlatformRenderTargetState State =>
            _impl._disposed ? PlatformRenderTargetState.Disposed : PlatformRenderTargetState.Ready;

        public ILockedFramebuffer Lock(
            IRenderTarget.RenderTargetSceneInfo sceneInfo,
            out FramebufferLockProperties properties)
        {
            properties = new FramebufferLockProperties(PreviousFrameIsRetained: _impl.Framebuffer.Produced);
            var locked = _impl.Framebuffer.Lock(_impl.OnFramePublished);
            if (locked is null)
            {
                throw new InvalidOperationException("The top-level's buffer has no CPU path.");
            }

            return locked;
        }

        public void Dispose()
        {
        }
    }
}
