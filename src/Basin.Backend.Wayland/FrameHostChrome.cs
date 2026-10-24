using Basin.Capabilities;
using static Basin.Backend.Wayland.WaylandBackendLog;

namespace Basin.Backend.Wayland;

/// <summary>
/// Draws a host frame through an <see cref="IFrameRenderer"/>. A press on the title moves the window, a double press maximizes it, and the edges resize it.
/// The close, maximize and minimize buttons act on release.
/// </summary>
public sealed class FrameHostChrome : HostFrameChrome
{
    private const uint DoubleClickMillis = 400;

    private readonly IFrameRenderer _renderer;
    private readonly Action _onClose;
    private readonly Action<string> _setCursor;

    private FramePart _hot = FramePart.None;
    private FramePart _pressed = FramePart.None;
    private uint _lastTitlePressMs;
    private bool _faulted;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameHostChrome"/> class and lays out the frame at once.
    /// </summary>
    /// <param name="frame">The host frame to draw.</param>
    /// <param name="host">The UI host that creates the chrome surface. It must match what <paramref name="renderer"/> draws into.</param>
    /// <param name="renderer">The frame renderer that measures, draws and hit-tests the chrome.</param>
    /// <param name="title">A title that replaces the output's own title, or <see langword="null"/> to follow <see cref="WaylandHostFrame.Title"/>.</param>
    /// <param name="appId">The application ID that the renderer receives in the frame state.</param>
    /// <param name="onClose">The action for the close button. <see langword="null"/> calls <see cref="WaylandHostFrame.RequestClose"/>.</param>
    /// <param name="setCursor">The action that shows a named cursor. <see langword="null"/> calls <see cref="WaylandHostFrame.ShowCursor(string)"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/>, <paramref name="host"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    public FrameHostChrome(
        WaylandHostFrame frame,
        IUIHost host,
        IFrameRenderer renderer,
        string? title = null,
        string appId = "dev.basin.compositor",
        Action? onClose = null,
        Action<string>? setCursor = null)
        : base(frame, host)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        FixedTitle = title;
        AppId = appId;
        _onClose = onClose ?? frame.RequestClose;
        _setCursor = setCursor ?? (name => frame.ShowCursor(name));
        Relayout();
    }

    /// <summary>Gets the title that replaces the output's title, or <see langword="null"/> when the chrome follows the output.</summary>
    public string? FixedTitle { get; }

    /// <summary>Gets the application ID that the renderer receives in the frame state.</summary>
    public string AppId { get; }

    /// <summary>
    /// Gets a value indicating whether a draw raised an exception. A faulted chrome clears the insets and never draws again.
    /// </summary>
    public bool Faulted => _faulted;

    /// <inheritdoc/>
    protected override void Relayout()
    {
        if (IsDisposed || _faulted)
        {
            return;
        }

        var state = BuildState();
        var insets = _renderer.Measure(state, Frame.Scale);
        var outsets = _renderer.Outsets(state, Frame.Scale);
        if (!Frame.SetInsets(
                new HostFrameInsets(insets.Top + outsets.Top, insets.Right + outsets.Right, insets.Bottom + outsets.Bottom, insets.Left + outsets.Left),
                new HostFrameInsets(outsets.Top, outsets.Right, outsets.Bottom, outsets.Left)))
        {
            return;
        }

        Repaint(state);
    }

    private void Repaint(in FrameState state)
    {
        if (IsDisposed || _faulted)
        {
            return;
        }

        var outerWidth = Frame.OuterWidth;
        var outerHeight = Frame.OuterHeight;
        var insets = Frame.Insets;
        if (outerWidth <= 0 || outerHeight <= 0 || insets.IsEmpty)
        {
            return;
        }

        try
        {
            if (!EnsureSurface())
            {
                return;
            }

            var hole = new Box(
                insets.Left,
                insets.Top,
                outerWidth - insets.Left - insets.Right,
                outerHeight - insets.Top - insets.Bottom);
            _renderer.Draw(Surface!, hole, state, new FrameInteraction(_hot, _pressed));
            Present();
        }
        catch (Exception e)
        {
            _faulted = true;
            Frame.SetInsets(default);
            Log.Error($"host frame fault: {e.Message}");
        }
    }

    private FrameState BuildState() => new()
    {
        Title = FixedTitle ?? Frame.Title,
        AppId = AppId,
        Active = Frame.Activated,
        Maximized = Frame.Maximized,
        Fullscreen = Frame.Fullscreen,
        Resizing = Frame.Resizing,
        Capabilities = FrameCapabilities.WindowMenu | FrameCapabilities.Maximize | FrameCapabilities.Minimize,
    };

    /// <inheritdoc/>
    protected override void OnPointerEnter(double x, double y)
    {
        _hot = FramePart.None;
        UpdateHover(x, y, force: true);
    }

    /// <inheritdoc/>
    protected override void OnPointerMotion(uint timeMs, double x, double y) => UpdateHover(x, y, force: false);

    /// <inheritdoc/>
    protected override void OnPointerLeave()
    {
        if (_hot == FramePart.None && _pressed == FramePart.None)
        {
            return;
        }

        _hot = FramePart.None;
        _pressed = FramePart.None;
        Repaint(BuildState());
    }

    private void UpdateHover(double x, double y, bool force)
    {
        var part = _renderer.PartAt(x, y, BuildState(), Frame.Scale);
        if (part == _hot && !force)
        {
            return;
        }

        var changed = part != _hot;
        _hot = part;
        _setCursor(_renderer.CursorFor(part) ?? CursorForPart(part));
        if (changed)
        {
            Repaint(BuildState());
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerButton(uint timeMs, uint button, bool pressed)
    {
        if (button != InputCodes.BtnLeft)
        {
            return;
        }

        if (!pressed)
        {
            if (_pressed != FramePart.None)
            {
                var released = _pressed;
                _pressed = FramePart.None;
                if (PointerInside && released == _hot)
                {
                    Activate(released);
                }

                Repaint(BuildState());
            }

            return;
        }

        var part = _renderer.PartAt(PointerX, PointerY, BuildState(), Frame.Scale);
        switch (part)
        {
            case FramePart.Title or FramePart.Icon:
                if (timeMs - _lastTitlePressMs <= DoubleClickMillis)
                {
                    _lastTitlePressMs = 0;
                    ToggleMaximize();
                    return;
                }

                _lastTitlePressMs = timeMs;
                Frame.StartMove();
                return;
            case FramePart.TopLeft or FramePart.Top or FramePart.TopRight or
                 FramePart.Right or FramePart.BottomRight or FramePart.Bottom or
                 FramePart.BottomLeft or FramePart.Left:
                Frame.StartResize(EdgesFor(part));
                return;
            case FramePart.None or FramePart.Border:
                return;
            default:
                _pressed = part;
                Repaint(BuildState());
                return;
        }
    }

    private void Activate(FramePart part)
    {
        switch (part)
        {
            case FramePart.Close:
                _onClose();
                break;
            case FramePart.Maximize:
                ToggleMaximize();
                break;
            case FramePart.Minimize:
                Frame.SetMinimized();
                break;
        }
    }

    private void ToggleMaximize() => Frame.SetMaximized(!Frame.Maximized);

    private static HostFrameEdges EdgesFor(FramePart part) => part switch
    {
        FramePart.Top => HostFrameEdges.Top,
        FramePart.Bottom => HostFrameEdges.Bottom,
        FramePart.Left => HostFrameEdges.Left,
        FramePart.Right => HostFrameEdges.Right,
        FramePart.TopLeft => HostFrameEdges.TopLeft,
        FramePart.TopRight => HostFrameEdges.TopRight,
        FramePart.BottomLeft => HostFrameEdges.BottomLeft,
        FramePart.BottomRight => HostFrameEdges.BottomRight,
        _ => HostFrameEdges.None,
    };

    private static string CursorForPart(FramePart part) => part switch
    {
        FramePart.Top => "top_side",
        FramePart.Bottom => "bottom_side",
        FramePart.Left => "left_side",
        FramePart.Right => "right_side",
        FramePart.TopLeft => "top_left_corner",
        FramePart.TopRight => "top_right_corner",
        FramePart.BottomLeft => "bottom_left_corner",
        FramePart.BottomRight => "bottom_right_corner",
        _ => "left_ptr",
    };
}
