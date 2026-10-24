using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// Server-side window decoration. An <see cref="IFrameRenderer"/> draws the chrome into one UI surface, and four scene buffers show its edges around the client.
/// The frame hit tests its parts, reports user actions through <see cref="Requested"/>, and animates on an <see cref="IFrameClock"/>.
/// </summary>
/// <remarks>
/// All members must be called on the thread that created the frame. A renderer or host exception faults the frame for good and raises <see cref="Faulted"/>.
/// </remarks>
public sealed class Frame : IUISurfaceObserver, ISceneInputFilter, IFrameSink, IAccessibleChrome, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly FrameHosts _hosts;
    private readonly IFrameRenderer _renderer;
    private readonly SceneTree _tree;
    private static readonly FramePart[] AccessibleParts = [FramePart.Menu, FramePart.Shade, FramePart.Minimize, FramePart.Maximize, FramePart.Close];
    private Box _describedBounds;
    private FrameState _describedState;
    private readonly SceneBuffer[] _strips;
    private readonly Box[] _stripBoxes;
    private readonly PixmanRegion32 _scratch = new();
    private readonly PixmanRegion32 _interactionDamage = new();
    private readonly PixmanRegion32 _backdrop = new();
    private readonly PixmanRegion32 _stripBackdrop = new();
    private readonly SceneBuffer _underlay;
    private readonly PixmanRegion32 _underlayRegion = new();
    private FrameBackdropShape _publishedShape;
    private bool _shapePublished;

    private IUISurface? _surface;
    private UIFrame _shown;
    private UIFrame _pending;
    private bool _hasPending;
    private bool _disposed;
    private bool _faulted;

    private Box _pendingGeometry;
    private FrameInsets _pendingInsets;
    private FrameInsets _pendingOutsets;
    private FrameState _pendingState;
    private double _pendingScale;

    private Box _shownGeometry;
    private FrameInsets _shownInsets;
    private FrameInsets _shownOutsets;
    private FrameState _shownState;
    private double _shownScale;
    private bool _shownValid;

    private FrameInteraction _interaction;
    private bool _animating;
    private bool _clockAttached;
    private FramePlacement _placement;
    private bool _fullAnimation;
    private bool _suppressSurfaceDamage;
    private uint _lastTitlePressMs;
    private bool _titleArmed;
    private int _touchLatch = -1;

    private IUISurface? _menuSurface;
    private SceneBuffer? _menuNode;
    private UIFrame _menuShown;
    private UISurfaceSize _menuSize;
    private int _menuHotItem = -1;
    private Point _menuSceneOffset;

    private Scene? _scene;

    /// <summary>
    /// Initializes a new instance of the <see cref="Frame"/> class that draws with one host.
    /// </summary>
    /// <param name="host">The host that creates the chrome surface.</param>
    /// <param name="renderer">The renderer that draws the chrome.</param>
    /// <param name="parent">The scene tree that holds the frame tree.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    public Frame(IUIHost host, IFrameRenderer renderer, SceneTree parent)
        : this(new FrameHosts(host), renderer, parent)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Frame"/> class.
    /// The first <see cref="Configure(in Box, double, in FrameState)"/> picks a host from <paramref name="hosts"/> and creates the surface.
    /// </summary>
    /// <param name="hosts">The hosts to choose from.</param>
    /// <param name="renderer">The renderer that draws the chrome.</param>
    /// <param name="parent">The scene tree that holds the frame tree.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hosts"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    public Frame(FrameHosts hosts, IFrameRenderer renderer, SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(renderer);
        _hosts = hosts;
        _renderer = renderer;
        _tree = new SceneTree(parent) { DebugName = "frame" };
        _underlay = new SceneBuffer(_tree) { Enabled = false, BackdropOnly = true, InputFilter = this, DebugName = "frame-underlay" };
        _strips = new SceneBuffer[4];
        _stripBoxes = new Box[4];
        for (var i = 0; i < 4; i++)
        {
            _strips[i] = new SceneBuffer(_tree) { Enabled = false, PreciseDamage = true, InputFilter = this, DebugName = "frame-strip" };
        }

        Register();
    }

    /// <summary>
    /// Gets or sets the client surface that the frame decorates. The frame does not use it. Scene diagnostics read it to name the window.
    /// </summary>
    public Surface? Window { get; set; }

    /// <summary>
    /// Gets the number of times the renderer has drawn the chrome.
    /// </summary>
    public long DrawCount { get; private set; }

    /// <summary>
    /// Gets the state of the last draw.
    /// </summary>
    public FrameState DrawnState { get; private set; }

    /// <summary>
    /// Gets the pointer interaction of the last draw.
    /// </summary>
    public FrameInteraction DrawnInteraction { get; private set; }

    private void Register()
    {
        if (_scene is null && _tree.RootOwner() is { } scene)
        {
            _scene = scene;
            scene.AddFrame(this);
        }
    }

    private void NoteDrawn(in FrameState state)
    {
        DrawCount++;
        DrawnState = state;
        DrawnInteraction = _interaction;
        _scene?.NotifyFrameDrawn(this);
        Drawn?.Invoke(state, _interaction);
    }

    /// <summary>
    /// Gets the tree that holds the edge strips and the underlay. The frame destroys it at disposal or on a fault.
    /// </summary>
    public SceneTree Tree => _tree;

    /// <summary>
    /// Gets the chrome buffer that the strips show, or <see langword="null"/> before the first <see cref="Commit"/>.
    /// </summary>
    public IBuffer? PresentedChrome => _shown.Buffer;

    /// <summary>
    /// Gets the chrome thickness on each side of the client from the last <see cref="Commit"/>, in logical pixels.
    /// </summary>
    public FrameInsets Insets { get; private set; }

    /// <summary>
    /// Gets the extra drawn margin outside the chrome, such as a shadow, from the last <see cref="Commit"/>, in logical pixels.
    /// </summary>
    public FrameInsets Outsets { get; private set; }

    /// <summary>
    /// Gets the box that the chrome surface covers, outsets included, from the last <see cref="Commit"/>. It is in parent tree coordinates.
    /// </summary>
    public Box DrawnBounds { get; private set; }

    /// <summary>
    /// Gets or sets the clock that paces animation. With no clock, the chrome does not animate. Set it before the first draw.
    /// </summary>
    public IFrameClock? Clock { get; set; }

    /// <summary>
    /// Gets or sets the time source in nanoseconds that stamps interaction changes. The default reads the monotonic clock.
    /// </summary>
    public Func<long> Time { get; set; } = static () => MonotonicClock.Nanos;

    /// <summary>
    /// Gets a value indicating whether the renderer reported that the shown chrome animates.
    /// </summary>
    public bool Animating => _animating;

    /// <summary>
    /// Gets the four edge strips in the order top, left, right, bottom. A strip with an empty box is disabled.
    /// </summary>
    public IReadOnlyList<SceneBuffer> StripNodes => _strips;

    /// <summary>
    /// Gets the box of one edge strip in chrome surface coordinates.
    /// </summary>
    /// <param name="index">The strip index from 0 to 3, in the order of <see cref="StripNodes"/>.</param>
    /// <returns>The strip box from the last <see cref="Commit"/>.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside 0 to 3.</exception>
    public Box StripBounds(int index) => _stripBoxes[index];

    /// <summary>
    /// Occurs after a <see cref="Commit"/> that changes the title, the active or maximized state, or the outer box, while a screen reader tree listens.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Describes the frame as a group named after the window title, with one button for each caption button that the renderer drew.
    /// It writes nothing before the first commit or while the frame is hidden.
    /// </summary>
    /// <param name="tree">The builder to write into.</param>
    public void Describe(AccessibleTreeBuilder tree)
    {
        _thread.Assert();
        if (!_shownValid || _faulted || _disposed || !_tree.Enabled)
        {
            return;
        }

        var state = _shownState;
        var name = state.Title is { Length: > 0 } title ? title : state.AppId;
        var frame = tree.Add(-1, new AccessibleNode(AccessibleRole.Group, name, ToScene(OuterBounds), state.Active ? AccessibleStates.Focused : AccessibleStates.None));
        var surfaceX = _shownGeometry.X - _shownInsets.Left - _shownOutsets.Left;
        var surfaceY = _shownGeometry.Y - _shownInsets.Top - _shownOutsets.Top;
        foreach (var part in AccessibleParts)
        {
            Box box;
            try
            {
                box = _renderer.PartBounds(part);
            }
            catch (Exception e)
            {
                Fault(e);
                return;
            }

            if (box.Width <= 0 || box.Height <= 0)
            {
                continue;
            }

            var label = part switch
            {
                FramePart.Close => "Close",
                FramePart.Maximize => state.Maximized ? "Restore" : "Maximize",
                FramePart.Minimize => "Minimize",
                FramePart.Shade => "Shade",
                _ => "Window menu",
            };
            var pressed = part;
            tree.Add(frame, new AccessibleNode(AccessibleRole.Button, label, ToScene(new Box(surfaceX + box.X, surfaceY + box.Y, box.Width, box.Height)), Press: () => Invoke(pressed)));
        }
    }

    /// <summary>
    /// Runs a caption button's action as a click on it does: <see cref="Requested"/> gets close, maximize, minimize or shade.
    /// A part with no action does nothing.
    /// </summary>
    /// <param name="part">The caption button.</param>
    public void Invoke(FramePart part)
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        FrameActionKind? kind = part switch
        {
            FramePart.Close => FrameActionKind.Close,
            FramePart.Maximize => FrameActionKind.ToggleMaximize,
            FramePart.Minimize => FrameActionKind.Minimize,
            FramePart.Shade => FrameActionKind.ToggleShade,
            _ => null,
        };
        if (kind is { } action)
        {
            Requested?.Invoke(new FrameAction(action));
        }
    }

    /// <summary>
    /// Gets the box of the client plus the chrome, outsets excluded, from the last <see cref="Commit"/>. It is in parent tree coordinates.
    /// </summary>
    public Box OuterBounds { get; private set; }

    /// <summary>
    /// Gets the scale of the shown chrome. It is 0 before the first <see cref="Commit"/>.
    /// </summary>
    public double Scale => _shownScale;

    /// <summary>
    /// Occurs at the end of each <see cref="Commit"/> that shows a new chrome buffer.
    /// </summary>
    public event Action? Committed;

    /// <summary>
    /// Gets a value indicating whether a renderer or host failure has stopped the frame.
    /// </summary>
    public bool IsFaulted => _faulted;

    /// <summary>
    /// Gets or sets a value indicating whether the frame tree is drawn. A hidden frame reports no parts.
    /// </summary>
    public bool Visible
    {
        get => _tree.Enabled;
        set => _tree.Enabled = value;
    }

    /// <summary>
    /// Occurs when the user asks for a window action through the chrome, for example move, resize, close or a menu item.
    /// The frame does not act on the request. The compositor owns that policy.
    /// </summary>
    public event Action<FrameAction>? Requested;

    /// <summary>
    /// Gets or sets the effect that the strips apply behind the region that the renderer reports as backdrop. It applies at the next <see cref="Commit"/>.
    /// </summary>
    public IBackdropEffect? BackdropEffect { get; set; }

    /// <summary>
    /// Gets or sets the key that <see cref="BackdropEffect"/> caches its state under. When it is <see langword="null"/>, each strip is its own key.
    /// </summary>
    public object? BackdropKey { get; set; }

    /// <summary>
    /// Gets or sets the effect for the underlay node, which covers the shape that the renderer reports. It applies at the next <see cref="Commit"/>.
    /// </summary>
    public IBackdropEffect? UnderlayEffect { get; set; }

    /// <summary>
    /// Gets or sets the key that <see cref="UnderlayEffect"/> caches its state under. When it is <see langword="null"/>, the underlay node is the key.
    /// </summary>
    public object? UnderlayKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the underlay also covers the client hole of the shape. The default is <see langword="true"/>.
    /// </summary>
    public bool UnderlayCoversClient { get; set; } = true;

    /// <summary>
    /// Gets the backdrop-only node that carries <see cref="UnderlayEffect"/>. It never accepts input.
    /// </summary>
    public SceneBuffer UnderlayNode => _underlay;

    /// <summary>
    /// Occurs when the underlay shape is first set or changes. It does not occur when no <see cref="UnderlayEffect"/> is set.
    /// </summary>
    public event Action<FrameBackdropShape>? ShapePublished;

    /// <summary>
    /// Occurs after each draw of the chrome, with the state and the interaction that the renderer drew.
    /// </summary>
    public event Action<FrameState, FrameInteraction>? Drawn;

    /// <summary>
    /// Gets or sets the tree that the window menu popup goes into. When it is <see langword="null"/>, <see cref="OpenMenu(double, double)"/> raises a <see cref="FrameActionKind.ShowMenu"/> request instead.
    /// </summary>
    public SceneTree? MenuLayer { get; set; }

    /// <summary>
    /// Gets or sets the box in menu layer coordinates that the menu popup must stay inside. An empty box sets no limit.
    /// </summary>
    public Box MenuConstraint { get; set; }

    /// <summary>
    /// Gets a value indicating whether the frame shows its own menu popup.
    /// </summary>
    public bool IsMenuOpen => _menuSurface is not null;

    /// <summary>
    /// Occurs once, when a renderer or host failure stops the frame. The frame tears down its surface and nodes before it raises the event.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// Asks the renderer for the chrome thickness for a state, without a draw. A layout can use it to size the client before <see cref="Configure(in Box, double, in FrameState)"/>.
    /// </summary>
    /// <param name="state">The state to measure.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The insets in logical pixels, or the default value after a fault or disposal.</returns>
    public FrameInsets Measure(in FrameState state, double scale)
    {
        _thread.Assert();
        if (_faulted || _disposed)
        {
            return default;
        }

        try
        {
            return _renderer.Measure(state, scale);
        }
        catch (Exception e)
        {
            Fault(e);
            return default;
        }
    }

    /// <summary>
    /// Gets or sets where the window sits in a layout. A change redraws the shown chrome when the renderer follows placement. It also overrides the placement of every later configured state.
    /// </summary>
    public FramePlacement Placement
    {
        get => _placement;
        set
        {
            _thread.Assert();
            if (_placement == value)
            {
                return;
            }

            _placement = value;
            RedrawPlacement();
        }
    }

    /// <summary>
    /// Draws the chrome for a client box into a pending buffer. The strips keep the old buffer until <see cref="Commit"/>, so the caller can match the change to a client commit.
    /// A call with the same geometry, scale and state as the pending or shown chrome does nothing.
    /// </summary>
    /// <param name="geometry">The client box in parent tree coordinates and logical pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="requested">The state to draw. Its placement is replaced by <see cref="Placement"/>.</param>
    public void Configure(in Box geometry, double scale, in FrameState requested)
    {
        _thread.Assert();
        var state = requested with { Placement = _placement };
        if (_faulted || _disposed || geometry.IsEmpty)
        {
            return;
        }

        if (_hasPending
            ? geometry == _pendingGeometry && scale == _pendingScale && state == _pendingState
            : _shownValid && geometry == _shownGeometry && scale == _shownScale && state == _shownState)
        {
            return;
        }

        FrameInsets insets;
        FrameInsets outsets;
        try
        {
            insets = _renderer.Measure(state, scale);
            outsets = _renderer.Outsets(state, scale);
        }
        catch (Exception e)
        {
            Fault(e);
            return;
        }

        var previousState = _hasPending ? _pendingState : _shownState;
        if ((_hasPending || _shownValid) && previousState.Active != state.Active)
        {
            var now = Time();
            _interaction = _interaction with { ActiveChangedAt = now, Now = now };
            _fullAnimation = true;
        }

        var outerWidth = geometry.Width + insets.Left + insets.Right + outsets.Left + outsets.Right;
        var outerHeight = geometry.Height + insets.Top + insets.Bottom + outsets.Top + outsets.Bottom;

        try
        {
            if (_surface is null)
            {
                if (_hosts.For(_renderer) is not { } host)
                {
                    Fault(new InvalidOperationException(
                        $"No UI host can draw this frame: {_hosts.Describe(_renderer)}."));
                    return;
                }

                var target = (host.Produces & UITargetKind.Dmabuf) != 0
                    ? UITargetKind.Dmabuf
                    : UITargetKind.Memory;
                _surface = host.CreateSurface(new UISurfaceOptions
                {
                    Target = target,
                    Width = outerWidth,
                    Height = outerHeight,
                    Scale = scale,
                });
                if (_surface is null)
                {
                    Fault(new InvalidOperationException("UI host declined to create a frame surface."));
                    return;
                }

                _surface.AddObserver(this);
            }
            else if (!_surface.Configure(outerWidth, outerHeight, scale))
            {
                return;
            }

            var clientBox = new Box(outsets.Left + insets.Left, outsets.Top + insets.Top, geometry.Width, geometry.Height);
            _interaction = _interaction with { Now = Time() };
            _renderer.Draw(_surface, clientBox, state, _interaction);
            NoteDrawn(state);
            UpdateAnimating(state);

            if (_hasPending)
            {
                _pending.Dispose();
                _hasPending = false;
            }

            _hasPending = _surface.TryAcquire(out _pending);
        }
        catch (Exception e)
        {
            Fault(e);
            return;
        }

        if (!_hasPending)
        {
            return;
        }

        _pendingGeometry = geometry;
        _pendingInsets = insets;
        _pendingOutsets = outsets;
        _pendingState = state;
        _pendingScale = scale;
    }

    /// <summary>
    /// Shows the pending chrome. It moves the tree, sets the strip boxes, applies the backdrop and underlay, closes the menu and raises <see cref="Committed"/>. It does nothing when no draw is pending.
    /// </summary>
    public void Commit()
    {
        _thread.Assert();
        if (!_hasPending || _faulted || _disposed)
        {
            return;
        }

        Register();

        var buffer = _pending.Buffer;
        if (buffer is null)
        {
            _pending.Dispose();
            _pending = default;
            _hasPending = false;
            return;
        }

        var insets = _pendingInsets;
        var outsets = _pendingOutsets;
        var geometry = _pendingGeometry;
        var outer = new Box(
            geometry.X - insets.Left,
            geometry.Y - insets.Top,
            geometry.Width + insets.Left + insets.Right,
            geometry.Height + insets.Top + insets.Bottom);
        var drawn = new Box(
            outer.X - outsets.Left,
            outer.Y - outsets.Top,
            outer.Width + outsets.Left + outsets.Right,
            outer.Height + outsets.Top + outsets.Bottom);

        _tree.SetPosition(drawn.X, drawn.Y);

        var top = outsets.Top + insets.Top;
        var bottom = outsets.Bottom + insets.Bottom;
        _stripBoxes[0] = new Box(0, 0, drawn.Width, top);
        _stripBoxes[1] = new Box(0, top, outsets.Left + insets.Left, geometry.Height);
        _stripBoxes[2] = new Box(drawn.Width - outsets.Right - insets.Right, top, outsets.Right + insets.Right, geometry.Height);
        _stripBoxes[3] = new Box(0, drawn.Height - bottom, drawn.Width, bottom);

        for (var i = 0; i < 4; i++)
        {
            var strip = _strips[i];
            var box = _stripBoxes[i];
            if (box.IsEmpty)
            {
                strip.Enabled = false;
                strip.SetBuffer(null);
                continue;
            }

            strip.SetBuffer(buffer);
            strip.AcquireFenceFd = _pending.AcquireFenceFd;
            strip.IsOpaque = _renderer.OpaqueChrome;
            strip.SourceBox = OutputScaling.ToPhysical(box, _pendingScale);
            strip.DestinationWidth = box.Width;
            strip.DestinationHeight = box.Height;
            strip.SetPosition(box.X, box.Y);
            strip.Enabled = true;
            strip.NotifyContentChanged();
        }

        ApplyBackdrop();
        ApplyUnderlay();

        _shown.Dispose();
        _shown = _pending;
        _pending = default;
        _hasPending = false;

        _shownGeometry = geometry;
        _shownInsets = insets;
        _shownOutsets = outsets;
        _shownState = _pendingState;
        _shownScale = _pendingScale;
        _shownValid = true;
        Insets = insets;
        Outsets = outsets;
        OuterBounds = outer;
        DrawnBounds = drawn;

        DismissMenu();
        Committed?.Invoke();
        if (Changed is not null && (outer != _describedBounds || _shownState.Title != _describedState.Title || _shownState.Active != _describedState.Active || _shownState.Maximized != _describedState.Maximized))
        {
            _describedBounds = outer;
            _describedState = _shownState;
            Changed.Invoke();
        }

        RedrawPlacement();
    }

    private void ApplyBackdrop()
    {
        var wanted = BackdropEffect is not null && !_faulted;
        _backdrop.Clear();
        if (wanted)
        {
            try
            {
                wanted = _renderer.BackdropRegion(_pendingState, _pendingScale, _backdrop);
            }
            catch (Exception e)
            {
                Fault(e);
                return;
            }

            var o = _pendingOutsets;
            if (wanted && o != default)
            {
                var g = _pendingGeometry;
                var i = _pendingInsets;
                _backdrop.IntersectRect(
                    _backdrop,
                    o.Left,
                    o.Top,
                    (uint)Math.Max(0, g.Width + i.Left + i.Right),
                    (uint)Math.Max(0, g.Height + i.Top + i.Bottom));
            }
        }

        for (var i = 0; i < 4; i++)
        {
            var strip = _strips[i];
            if (strip.IsDestroyed)
            {
                continue;
            }

            if (!wanted || !strip.Enabled)
            {
                strip.SetBackdropEffect(null, null);
                continue;
            }

            var box = _stripBoxes[i];
            _stripBackdrop.IntersectRect(_backdrop, box.X, box.Y, (uint)box.Width, (uint)box.Height);
            if (_stripBackdrop.IsEmpty)
            {
                strip.SetBackdropEffect(null, null);
                continue;
            }

            _stripBackdrop.Translate(-box.X, -box.Y);
            strip.SetBackdropEffect(BackdropEffect, _stripBackdrop, BackdropKey ?? strip);
        }
    }

    private void ApplyUnderlay()
    {
        if (_underlay.IsDestroyed)
        {
            return;
        }

        var shape = default(FrameBackdropShape);
        var wanted = UnderlayEffect is not null && !_faulted;
        if (wanted)
        {
            try
            {
                wanted = _renderer.BackdropShape(_pendingState, _pendingScale, out shape);
            }
            catch (Exception e)
            {
                Fault(e);
                return;
            }
        }

        if (!wanted)
        {
            _underlay.Enabled = false;
            _underlay.SetBackdropEffect(null, null);
            _shapePublished = false;
            return;
        }

        var o = _pendingOutsets;
        var i = _pendingInsets;
        var width = _pendingGeometry.Width + i.Left + i.Right;
        var height = _pendingGeometry.Height + i.Top + i.Bottom;
        _underlay.SetPosition(o.Left, o.Top);
        _underlay.DestinationWidth = width;
        _underlay.DestinationHeight = height;
        RoundedRegion.Fill(_underlayRegion, width, height, (int)Math.Round(Math.Max(0, shape.CornerRadius)));
        var hole = shape.ClientHole;
        if (!UnderlayCoversClient && !hole.IsEmpty)
        {
            _scratch.Reset(new PixmanBox32(hole.X, hole.Y, hole.Right, hole.Bottom));
            _underlayRegion.Subtract(_underlayRegion, _scratch);
        }

        _underlay.Enabled = true;
        _underlay.SetBackdropEffect(UnderlayEffect, _underlayRegion, UnderlayKey ?? _underlay);
        _shapePublished = false;
        PublishShape(shape);
    }

    private void PublishShown()
    {
        if (!_underlay.Enabled || UnderlayEffect is null || _underlay.IsDestroyed)
        {
            return;
        }

        if (_renderer.BackdropShape(_shownState, _shownScale, out var shape))
        {
            PublishShape(shape);
        }
    }

    private void PublishShape(in FrameBackdropShape shape)
    {
        if (_shapePublished && shape == _publishedShape)
        {
            return;
        }

        var first = !_shapePublished;
        _publishedShape = shape;
        _shapePublished = true;
        if (UnderlayEffect is IShapedBackdropEffect shaped)
        {
            shaped.SetShape(UnderlayKey ?? _underlay, shape);
        }

        ShapePublished?.Invoke(shape);

        if (!first)
        {
            _scratch.Reset(new PixmanBox32(0, 0, _underlay.DestinationWidth, _underlay.DestinationHeight));
            _underlay.NotifyContentChanged(_scratch);
        }
    }

    /// <summary>
    /// Checks whether a pending draw matches a client box and scale.
    /// </summary>
    /// <param name="geometry">The client box to compare.</param>
    /// <param name="scale">The scale to compare.</param>
    /// <returns><see langword="true"/> when a <see cref="Commit"/> shows chrome for that box and scale.</returns>
    public bool HasPendingFor(in Box geometry, double scale)
    {
        _thread.Assert();
        return _hasPending && _pendingGeometry == geometry && _pendingScale == scale;
    }

    /// <summary>
    /// Checks whether a scene node is in the frame tree. The menu popup is not in the frame tree.
    /// </summary>
    /// <param name="node">The node to check, for example the result of a hit test.</param>
    /// <returns><see langword="true"/> when <paramref name="node"/> is <see cref="Tree"/> or one of its descendants.</returns>
    public bool OwnsNode(SceneNode node)
    {
        _thread.Assert();
        for (var candidate = node; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate == _tree)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the chrome part at a point.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <returns><see cref="FramePart.None"/> inside the client, outside the chrome, before the first commit, or when the frame is hidden, faulted or disposed.</returns>
    public FramePart PartAt(double x, double y)
    {
        _thread.Assert();
        if (!_shownValid || _faulted || _disposed || !_tree.Enabled)
        {
            return FramePart.None;
        }

        var o = _shownOutsets;
        var sx = x - (_shownGeometry.X - _shownInsets.Left - o.Left);
        var sy = y - (_shownGeometry.Y - _shownInsets.Top - o.Top);
        return SurfacePartAt(sx, sy);
    }

    private Box ToScene(in Box box) => _tree.Parent is { } parent ? parent.MapToScene(box) : box;

    private FramePart SurfacePartAt(double sx, double sy)
    {
        var o = _shownOutsets;
        var outerWidth = _shownGeometry.Width + _shownInsets.Left + _shownInsets.Right;
        var outerHeight = _shownGeometry.Height + _shownInsets.Top + _shownInsets.Bottom;
        if (sx < o.Left || sy < o.Top || sx >= o.Left + outerWidth || sy >= o.Top + outerHeight)
        {
            return FramePart.None;
        }

        var clientX = o.Left + _shownInsets.Left;
        var clientY = o.Top + _shownInsets.Top;
        if (sx >= clientX && sx < clientX + _shownGeometry.Width &&
            sy >= clientY && sy < clientY + _shownGeometry.Height)
        {
            return FramePart.None;
        }

        try
        {
            return _renderer.PartAt(sx, sy, _shownState, _shownScale);
        }
        catch (Exception e)
        {
            Fault(e);
            return FramePart.None;
        }
    }

    /// <summary>
    /// Lets a strip accept input only where the renderer reports a part, so transparent chrome and shadows pass input through. The underlay never accepts input.
    /// </summary>
    /// <param name="buffer">The node that the hit test reached.</param>
    /// <param name="x">The horizontal position in node-local coordinates.</param>
    /// <param name="y">The vertical position in node-local coordinates.</param>
    /// <returns><see langword="true"/> when the point is on a part, or when <paramref name="buffer"/> is not a frame node.</returns>
    public bool AcceptsInputAt(SceneBuffer buffer, double x, double y)
    {
        _thread.Assert();
        if (!_shownValid || _faulted || _disposed)
        {
            return false;
        }

        if (buffer == _underlay)
        {
            return false;
        }

        var index = Array.IndexOf(_strips, buffer);
        if (index < 0)
        {
            return true;
        }

        var box = _stripBoxes[index];
        return SurfacePartAt(box.X + x, box.Y + y) != FramePart.None;
    }

    /// <summary>
    /// Gets the cursor name that the renderer wants for the part at a point.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <returns>The cursor name, or <see langword="null"/> when no part is there.</returns>
    public string? CursorAt(double x, double y)
    {
        _thread.Assert();
        var part = PartAt(x, y);
        if (part == FramePart.None || _faulted)
        {
            return null;
        }

        try
        {
            return _renderer.CursorFor(part);
        }
        catch (Exception e)
        {
            Fault(e);
            return null;
        }
    }

    /// <summary>
    /// Reports that the pointer entered the frame. It acts the same as <see cref="PointerMotion(double, double)"/>.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    public void PointerEnter(double x, double y) => PointerMotion(x, y);

    /// <summary>
    /// Reports pointer motion. A change of the hot part redraws the parts that changed. Pointer-following renderers start to animate.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    public void PointerMotion(double x, double y)
    {
        _thread.Assert();
        var part = PartAt(x, y);
        TrackPointer(x, y, inside: true);
        if (part != _interaction.Hot)
        {
            var previous = _interaction.Hot;
            SetHot(part);
            RedrawShown(previous, part);
        }

        FollowPointer();
    }

    /// <summary>
    /// Reports that the pointer left the frame. It clears the hot and pressed parts and redraws them.
    /// </summary>
    public void PointerLeave()
    {
        _thread.Assert();
        var wasInside = _interaction.PointerInside;
        if (wasInside)
        {
            _interaction = _interaction with { PointerInside = false, PointerChangedAt = Time() };
        }

        if (_interaction.Hot != FramePart.None || _interaction.Pressed != FramePart.None)
        {
            var previous = _interaction;
            SetHot(FramePart.None);
            SetPressed(FramePart.None);
            RedrawShown(previous.Hot, previous.Pressed);
        }

        if (wasInside)
        {
            FollowPointer();
        }
    }

    private void TrackPointer(double x, double y, bool inside)
    {
        if (!_shownValid)
        {
            return;
        }

        var sx = x - (_shownGeometry.X - _shownInsets.Left - _shownOutsets.Left);
        var sy = y - (_shownGeometry.Y - _shownInsets.Top - _shownOutsets.Top);
        if (_interaction.PointerInside == inside && _interaction.PointerX == sx && _interaction.PointerY == sy)
        {
            return;
        }

        _interaction = _interaction with { PointerX = sx, PointerY = sy, PointerInside = inside, PointerChangedAt = Time() };
    }

    private void FollowPointer()
    {
        if (!_shownValid || _hasPending || _surface is null || _faulted || _disposed || _animating)
        {
            return;
        }

        try
        {
            _interaction = _interaction with { Now = Time() };
            if (!_renderer.Animating(_shownState, _interaction))
            {
                return;
            }

            _fullAnimation = true;
            UpdateAnimating(_shownState);
            if (_animating && _clockAttached && Clock is { } clock)
            {
                clock.RequestFrame();
            }
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Reports a pointer button. A press on the title or border requests a move, a press on an edge requests a resize, and a second title press inside 400 ms requests maximize.
    /// A button fires its action on release over the same part. It does nothing while a touch point is latched.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <param name="timeMs">The event time in milliseconds, or 0 to disable double-click detection.</param>
    public void PointerButton(double x, double y, bool pressed, uint timeMs = 0)
    {
        _thread.Assert();
        if (_touchLatch >= 0)
        {
            return;
        }

        var part = PartAt(x, y);
        if (pressed)
        {
            switch (part)
            {
                case FramePart.Close:
                case FramePart.Maximize:
                case FramePart.Minimize:
                case FramePart.Shade:
                case FramePart.Above:
                case FramePart.Stick:
                case FramePart.Menu:
                case FramePart.Icon:
                    _titleArmed = false;
                    if (_interaction.Pressed != part)
                    {
                        SetPressed(part);
                        RedrawShown(part, FramePart.None);
                    }

                    return;
                case FramePart.Title:
                    if (_titleArmed && timeMs != 0 && timeMs - _lastTitlePressMs <= DoubleClickMs)
                    {
                        _titleArmed = false;
                        Requested?.Invoke(new FrameAction(FrameActionKind.ToggleMaximize));
                        return;
                    }

                    _titleArmed = true;
                    _lastTitlePressMs = timeMs;
                    Requested?.Invoke(new FrameAction(FrameActionKind.Move));
                    return;
                case FramePart.Border:
                    _titleArmed = false;
                    Requested?.Invoke(new FrameAction(FrameActionKind.Move));
                    return;
                case FramePart.None:
                    return;
                default:
                    _titleArmed = false;
                    Requested?.Invoke(new FrameAction(FrameActionKind.Resize, EdgesOf(part)));
                    return;
            }
        }

        var released = _interaction.Pressed;
        if (released == FramePart.None)
        {
            return;
        }

        SetPressed(FramePart.None);
        RedrawShown(released, FramePart.None);
        if (part != released)
        {
            return;
        }

        switch (released)
        {
            case FramePart.Close:
                Requested?.Invoke(new FrameAction(FrameActionKind.Close));
                break;
            case FramePart.Maximize:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleMaximize));
                break;
            case FramePart.Minimize:
                Requested?.Invoke(new FrameAction(FrameActionKind.Minimize));
                break;
            case FramePart.Shade:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleShade));
                break;
            case FramePart.Above:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleAbove));
                break;
            case FramePart.Stick:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleSticky));
                break;
            case FramePart.Menu:
            case FramePart.Icon:
                OpenMenu(x, y);
                break;
        }
    }

    private const uint DoubleClickMs = 400;

    /// <summary>
    /// Gets or sets the distance in logical pixels outside the chrome that a touch still finds the nearest resize edge. The default is 0.
    /// </summary>
    public double TouchSlop { get; set; }

    /// <summary>
    /// Reports a touch down. It acts like a pointer press, uses <see cref="TouchSlop"/> to find an edge, and latches the touch on a button part.
    /// It does nothing while a touch is latched or a button is pressed.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="timeMs">The event time in milliseconds, or 0 to disable double-tap detection.</param>
    public void TouchDown(double x, double y, int id, uint timeMs = 0)
    {
        _thread.Assert();
        if (_touchLatch >= 0 || _interaction.Pressed != FramePart.None)
        {
            return;
        }

        var part = TouchPartAt(x, y);
        switch (part)
        {
            case FramePart.Close:
            case FramePart.Maximize:
            case FramePart.Minimize:
            case FramePart.Shade:
            case FramePart.Above:
            case FramePart.Stick:
            case FramePart.Menu:
            case FramePart.Icon:
                _titleArmed = false;
                _touchLatch = id;
                SetPressed(part);
                RedrawShown(part, FramePart.None);
                return;
            case FramePart.Title:
                if (_titleArmed && timeMs != 0 && timeMs - _lastTitlePressMs <= DoubleClickMs)
                {
                    _titleArmed = false;
                    Requested?.Invoke(new FrameAction(FrameActionKind.ToggleMaximize));
                    return;
                }

                _titleArmed = true;
                _lastTitlePressMs = timeMs;
                Requested?.Invoke(new FrameAction(FrameActionKind.Move));
                return;
            case FramePart.Border:
                _titleArmed = false;
                Requested?.Invoke(new FrameAction(FrameActionKind.Move));
                return;
            case FramePart.None:
                return;
            default:
                _titleArmed = false;
                Requested?.Invoke(new FrameAction(FrameActionKind.Resize, EdgesOf(part)));
                return;
        }
    }

    /// <summary>
    /// Reports a touch up. The latched button fires its action when the touch ends over the same part. Other ids are ignored.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="id">The touch point id.</param>
    public void TouchUp(double x, double y, int id)
    {
        _thread.Assert();
        if (_touchLatch != id)
        {
            return;
        }

        _touchLatch = -1;
        var released = _interaction.Pressed;
        if (released == FramePart.None)
        {
            return;
        }

        SetPressed(FramePart.None);
        RedrawShown(released, FramePart.None);
        if (TouchPartAt(x, y) != released)
        {
            return;
        }

        switch (released)
        {
            case FramePart.Close:
                Requested?.Invoke(new FrameAction(FrameActionKind.Close));
                break;
            case FramePart.Maximize:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleMaximize));
                break;
            case FramePart.Minimize:
                Requested?.Invoke(new FrameAction(FrameActionKind.Minimize));
                break;
            case FramePart.Shade:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleShade));
                break;
            case FramePart.Above:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleAbove));
                break;
            case FramePart.Stick:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleSticky));
                break;
            case FramePart.Menu:
            case FramePart.Icon:
                OpenMenu(x, y);
                break;
        }
    }

    /// <summary>
    /// Cancels the latched touch and releases its button with no action.
    /// </summary>
    public void TouchCancel()
    {
        _thread.Assert();
        if (_touchLatch < 0)
        {
            return;
        }

        _touchLatch = -1;
        var released = _interaction.Pressed;
        if (released == FramePart.None)
        {
            return;
        }

        SetPressed(FramePart.None);
        RedrawShown(released, FramePart.None);
    }

    private FramePart TouchPartAt(double x, double y)
    {
        var part = PartAt(x, y);
        if (TouchSlop <= 0 || part != FramePart.None || !_shownValid)
        {
            return part;
        }

        var left = _shownGeometry.X - _shownInsets.Left;
        var top = _shownGeometry.Y - _shownInsets.Top;
        var right = left + _shownGeometry.Width + _shownInsets.Left + _shownInsets.Right;
        var bottom = top + _shownGeometry.Height + _shownInsets.Top + _shownInsets.Bottom;
        if (x < left - TouchSlop || x > right + TouchSlop ||
            y < top - TouchSlop || y > bottom + TouchSlop)
        {
            return part;
        }

        var near = PartAt(Math.Clamp(x, left, right - 1), Math.Clamp(y, top, bottom - 1));
        return EdgesOf(near) != FrameEdges.None ? near : part;
    }

    /// <summary>
    /// Gets or sets the offset from the frame input coordinates to <see cref="MenuLayer"/> coordinates.
    /// </summary>
    public Point MenuOrigin { get; set; }

    /// <summary>
    /// Opens the window menu at a point. When the renderer has no menu, no <see cref="MenuLayer"/> is set or the popup fails, it raises a <see cref="FrameActionKind.ShowMenu"/> request instead.
    /// </summary>
    /// <param name="x">The horizontal position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    /// <param name="y">The vertical position in the coordinates of the parent tree, the same space as the configured geometry.</param>
    public void OpenMenu(double x, double y)
    {
        _thread.Assert();
        if (_faulted || _disposed || !_shownValid)
        {
            return;
        }

        DismissMenu();
        try
        {
            _menuSize = _renderer.MeasureMenu(_shownState, _shownScale);
            if (MenuLayer is null || _menuSize.Width <= 0 || _menuSize.Height <= 0 || _surface is null)
            {
                Requested?.Invoke(new FrameAction(FrameActionKind.ShowMenu));
                return;
            }

            var anchor = new Box((int)x, (int)y, 1, 1);
            var popup = _surface.CreatePopup(anchor, UIPopupGravity.BottomRight);
            if (popup is null || !popup.Configure(_menuSize.Width, _menuSize.Height, _shownScale))
            {
                popup?.Dispose();
                Requested?.Invoke(new FrameAction(FrameActionKind.ShowMenu));
                return;
            }

            _menuSurface = popup;
            _menuHotItem = -1;
            _renderer.DrawMenu(popup, _shownState, hotItem: -1);
            if (!popup.TryAcquire(out _menuShown) || _menuShown.Buffer is null)
            {
                DismissMenu();
                return;
            }

            var position = new Point(MenuOrigin.X + (int)x, MenuOrigin.Y + (int)y);
            if (!MenuConstraint.IsEmpty)
            {
                position = new Point(
                    Math.Max(MenuConstraint.X, Math.Min(position.X, MenuConstraint.Right - _menuSize.Width)),
                    Math.Max(MenuConstraint.Y, Math.Min(position.Y, MenuConstraint.Bottom - _menuSize.Height)));
            }

            _menuSceneOffset = position;
            popup.AddObserver(this);
            _menuNode = new SceneBuffer(MenuLayer) { PreciseDamage = true };
            _menuNode.SetBuffer(_menuShown.Buffer);
            _menuNode.SourceBox = OutputScaling.ToPhysical(new Box(0, 0, _menuSize.Width, _menuSize.Height), _shownScale);
            _menuNode.DestinationWidth = _menuSize.Width;
            _menuNode.DestinationHeight = _menuSize.Height;
            _menuNode.SetPosition(position.X, position.Y);
            _menuNode.RaiseToTop();
            _menuNode.NotifyContentChanged();
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Closes the menu popup and destroys its node. It does nothing when no menu is open.
    /// </summary>
    public void DismissMenu()
    {
        _thread.Assert();
        if (_menuSurface is null)
        {
            return;
        }

        var popup = _menuSurface;
        popup.RemoveObserver(this);
        ForgetMenu();
        try
        {
            popup.Dispose();
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Checks whether a scene node is the menu popup of this frame.
    /// </summary>
    /// <param name="node">The node to check, for example the result of a hit test.</param>
    /// <returns><see langword="true"/> when a menu is open and <paramref name="node"/> shows it.</returns>
    public bool OwnsMenuNode(SceneNode node) => _menuNode is not null && node == _menuNode;

    /// <summary>
    /// Reports pointer motion over the menu popup and redraws it when the hot item changes.
    /// </summary>
    /// <param name="x">The horizontal position in menu-local coordinates.</param>
    /// <param name="y">The vertical position in menu-local coordinates.</param>
    public void MenuPointerMotion(double x, double y)
    {
        _thread.Assert();
        if (_menuSurface is null || _faulted)
        {
            return;
        }

        try
        {
            var item = _renderer.MenuItemAt(x, y, _shownState, _shownScale);
            if (item != _menuHotItem)
            {
                _menuHotItem = item;
                _renderer.DrawMenu(_menuSurface, _shownState, item);
            }
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Reports that the pointer left the menu popup and clears the hot item.
    /// </summary>
    public void MenuPointerLeave()
    {
        _thread.Assert();
        if (_menuSurface is null || _menuHotItem == -1 || _faulted)
        {
            return;
        }

        try
        {
            _menuHotItem = -1;
            _renderer.DrawMenu(_menuSurface, _shownState, hotItem: -1);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Reports a button over the menu popup. A release closes the menu and raises <see cref="Requested"/> with the action of the item under the pointer, if any. A press does nothing.
    /// </summary>
    /// <param name="x">The horizontal position in menu-local coordinates.</param>
    /// <param name="y">The vertical position in menu-local coordinates.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    public void MenuPointerButton(double x, double y, bool pressed)
    {
        _thread.Assert();
        if (_menuSurface is null || _faulted)
        {
            return;
        }

        if (pressed)
        {
            return;
        }

        FrameAction? action = null;
        try
        {
            var item = _renderer.MenuItemAt(x, y, _shownState, _shownScale);
            if (item >= 0)
            {
                action = _renderer.MenuItemAction(item, _shownState);
            }
        }
        catch (Exception e)
        {
            Fault(e);
            return;
        }

        DismissMenu();
        if (action is { } chosen)
        {
            Requested?.Invoke(chosen);
        }
    }

    /// <summary>
    /// Forwards damage from the chrome surface to the strips, or from the menu popup to its node.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    /// <param name="damage">The changed region. An empty region damages everything.</param>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
        if (ReferenceEquals(surface, _surface))
        {
            OnSurfaceDamaged(damage);
        }
        else
        {
            OnMenuDamaged(damage);
        }
    }

    /// <summary>
    /// Drops a surface that its host disposed first. A lost chrome surface hides the strips, and the next draw asks the hosts for a new surface.
    /// A lost menu popup closes the menu.
    /// </summary>
    /// <param name="surface">The disposed surface.</param>
    public void OnSurfaceDestroyed(IUISurface surface)
    {
        _thread.Assert();
        if (surface is null)
        {
            return;
        }

        if (ReferenceEquals(surface, _menuSurface))
        {
            ForgetMenu();
            return;
        }

        if (!ReferenceEquals(surface, _surface))
        {
            return;
        }

        if (_menuSurface is { } popup)
        {
            ForgetMenu();
            popup.RemoveObserver(this);
        }

        _surface = null;
        if (_hasPending)
        {
            _pending.Dispose();
            _pending = default;
            _hasPending = false;
        }

        foreach (var strip in _strips)
        {
            if (!strip.IsDestroyed)
            {
                strip.Enabled = false;
                strip.AcquireFenceFd = -1;
                strip.SetBuffer(null);
            }
        }

        _shown.Dispose();
        _shown = default;
        _shownValid = false;
    }

    private void ForgetMenu()
    {
        _menuSurface = null;
        _menuNode?.Destroy();
        _menuNode = null;
        _menuShown.Dispose();
        _menuShown = default;
        _menuHotItem = -1;
    }

    private void OnMenuDamaged(PixmanRegion32 region)
    {
        if (_menuNode is null || _menuNode.IsDestroyed)
        {
            return;
        }

        if (region.IsEmpty)
        {
            _menuNode.NotifyContentChanged();
        }
        else
        {
            _menuNode.NotifyContentChanged(region);
        }
    }

    private void SetHot(FramePart part)
    {
        if (_interaction.Hot == part)
        {
            return;
        }

        _interaction = _interaction with { PreviousHot = _interaction.Hot, Hot = part, HoverChangedAt = Time() };
    }

    private void SetPressed(FramePart part)
    {
        if (_interaction.Pressed == part)
        {
            return;
        }

        _interaction = _interaction with { PreviousPressed = _interaction.Pressed, Pressed = part, PressChangedAt = Time() };
    }

    /// <summary>
    /// Advances the chrome animation to the predicted vblank through <see cref="Tick(long)"/>. A value of 0 means that the vblank is not known, and the frame ticks at <see cref="Time"/> instead.
    /// </summary>
    /// <param name="output">The output that the frame is for.</param>
    /// <param name="predictedVblankNanos">The predicted vblank on the monotonic clock in nanoseconds, or 0.</param>
    public void BeginFrame(IOutput output, long predictedVblankNanos) => Tick(predictedVblankNanos > 0 ? predictedVblankNanos : Time());

    /// <summary>
    /// Does nothing. The frame paces its animation only on <see cref="BeginFrame(IOutput, long)"/>.
    /// </summary>
    /// <param name="output">The output that presented the frame.</param>
    /// <param name="presentedNanos">The presentation time in nanoseconds.</param>
    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    /// <summary>
    /// Redraws an animating chrome at a time and asks the clock for another frame while it still animates. It does nothing while a draw is pending.
    /// </summary>
    /// <param name="now">The animation time on the monotonic clock in nanoseconds.</param>
    public void Tick(long now)
    {
        _thread.Assert();
        if (!_animating || !_shownValid || _hasPending || _surface is null || _faulted || _disposed)
        {
            return;
        }

        try
        {
            _interactionDamage.Clear();
            if (!_fullAnimation)
            {
                AddPartBounds(_interaction.Hot);
                AddPartBounds(_interaction.PreviousHot);
                AddPartBounds(_interaction.Pressed);
                AddPartBounds(_interaction.PreviousPressed);
            }

            _interaction = _interaction with { Now = now };
            if (!DrawShown())
            {
                return;
            }

            if (_fullAnimation)
            {
                _interactionDamage.Clear();
            }

            DamageStrips(_interactionDamage);
            PublishShown();
            UpdateAnimating(_shownState);
            if (_animating && _clockAttached && Clock is { } clock)
            {
                clock.RequestFrame();
            }
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    private void RedrawPlacement()
    {
        if (!_shownValid || _hasPending || _surface is null || _faulted || _disposed || _shownState.Placement == _placement)
        {
            return;
        }

        _shownState = _shownState with { Placement = _placement };
        if (!_renderer.FollowsPlacement)
        {
            return;
        }

        try
        {
            _interaction = _interaction with { Now = Time() };
            _interactionDamage.Clear();
            if (DrawShown())
            {
                DamageStrips(_interactionDamage);
                PublishShown();
            }
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    private bool DrawShown()
    {
        var surface = _surface!;
        if (_shown.Buffer is not { } current || !current.TryGetDmabuf(out _))
        {
            _suppressSurfaceDamage = true;
            try
            {
                _renderer.Draw(surface, ShownClientBox(), _shownState, _interaction);
                NoteDrawn(_shownState);
            }
            finally
            {
                _suppressSurfaceDamage = false;
            }

            return true;
        }

        var width = _shownGeometry.Width + _shownInsets.Left + _shownInsets.Right + _shownOutsets.Left + _shownOutsets.Right;
        var height = _shownGeometry.Height + _shownInsets.Top + _shownInsets.Bottom + _shownOutsets.Top + _shownOutsets.Bottom;
        if (!surface.Configure(width, height, _shownScale))
        {
            return false;
        }

        _suppressSurfaceDamage = true;
        try
        {
            _renderer.Draw(surface, ShownClientBox(), _shownState, _interaction);
                NoteDrawn(_shownState);
        }
        finally
        {
            _suppressSurfaceDamage = false;
        }

        if (!surface.TryAcquire(out var frame) || frame.Buffer is not { } buffer)
        {
            frame.Dispose();
            return true;
        }

        foreach (var strip in _strips)
        {
            if (strip.Enabled && !strip.IsDestroyed)
            {
                strip.SetBuffer(buffer);
                strip.AcquireFenceFd = frame.AcquireFenceFd;
            }
        }

        _shown.Dispose();
        _shown = frame;
        return true;
    }

    private Box ShownClientBox() => new(
        _shownOutsets.Left + _shownInsets.Left,
        _shownOutsets.Top + _shownInsets.Top,
        _shownGeometry.Width,
        _shownGeometry.Height);

    private void UpdateAnimating(in FrameState state)
    {
        var animating = _renderer.Animating(state, _interaction);
        if (!animating)
        {
            _fullAnimation = false;
        }

        if (animating == _animating)
        {
            return;
        }

        _animating = animating;
        if (Clock is not { } clock)
        {
            return;
        }

        if (animating && !_clockAttached)
        {
            clock.Add(this);
            _clockAttached = true;
        }
        else if (!animating && _clockAttached)
        {
            clock.Remove(this);
            _clockAttached = false;
        }
    }

    private void DetachClock()
    {
        _animating = false;
        if (_clockAttached && Clock is { } clock)
        {
            clock.Remove(this);
        }

        _clockAttached = false;
    }

    /// <summary>
    /// Maps a resize part to the edges that it moves.
    /// </summary>
    /// <param name="part">The part to map.</param>
    /// <returns>The edges, or <see cref="FrameEdges.None"/> for a part that does not resize.</returns>
    public static FrameEdges EdgesOf(FramePart part) => part switch
    {
        FramePart.Top => FrameEdges.Top,
        FramePart.Bottom => FrameEdges.Bottom,
        FramePart.Left => FrameEdges.Left,
        FramePart.Right => FrameEdges.Right,
        FramePart.TopLeft => FrameEdges.TopLeft,
        FramePart.TopRight => FrameEdges.TopRight,
        FramePart.BottomLeft => FrameEdges.BottomLeft,
        FramePart.BottomRight => FrameEdges.BottomRight,
        _ => FrameEdges.None,
    };

    /// <summary>
    /// Removes the frame from its scene, closes the menu, disposes the chrome surface and destroys the frame tree. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene?.RemoveFrame(this);
        _scene = null;
        TearDown();
        _scratch.Dispose();
        _interactionDamage.Dispose();
        _backdrop.Dispose();
        _stripBackdrop.Dispose();
        _underlayRegion.Dispose();
    }

    private void RedrawShown(FramePart changed, FramePart alsoChanged)
    {
        if (!_shownValid || _hasPending || _surface is null || _faulted || _disposed)
        {
            return;
        }

        try
        {
            _interactionDamage.Clear();
            AddPartBounds(changed);
            AddPartBounds(alsoChanged);
            if (_interactionDamage.IsEmpty)
            {
                return;
            }

            _interaction = _interaction with { Now = Time() };
            if (!DrawShown())
            {
                return;
            }

            DamageStrips(_interactionDamage);
            PublishShown();
            UpdateAnimating(_shownState);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    private void AddPartBounds(FramePart part)
    {
        if (part == FramePart.None)
        {
            return;
        }

        var bounds = _renderer.PartBounds(part);
        if (!bounds.IsEmpty)
        {
            _interactionDamage.UnionRect(_interactionDamage, bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height);
        }
    }

    private void OnSurfaceDamaged(PixmanRegion32 region)
    {
        if (_suppressSurfaceDamage)
        {
            return;
        }

        DamageStrips(region);
    }

    private void DamageStrips(PixmanRegion32 region)
    {
        if (_disposed || _faulted)
        {
            return;
        }

        for (var i = 0; i < 4; i++)
        {
            var strip = _strips[i];
            if (!strip.Enabled)
            {
                continue;
            }

            if (region.IsEmpty)
            {
                strip.NotifyContentChanged();
                continue;
            }

            var box = _stripBoxes[i];
            _scratch.IntersectRect(region, box.X, box.Y, (uint)box.Width, (uint)box.Height);
            if (_scratch.IsEmpty)
            {
                continue;
            }

            _scratch.Translate(-box.X, -box.Y);
            strip.NotifyContentChanged(_scratch);
        }
    }

    private void Fault(Exception e)
    {
        if (_faulted)
        {
            return;
        }

        _faulted = true;
        TearDown();
        Faulted?.Invoke(e);
    }

    private void TearDown()
    {
        DetachClock();
        DismissMenu();
        if (UnderlayEffect is { } underlay)
        {
            try
            {
                underlay.ForgetSurface(UnderlayKey ?? _underlay);
            }
            catch (Exception e)
            {
                SceneLog.Log.Warn(e, $"frame: underlay effect failed to forget the frame surface at teardown");
            }
        }

        foreach (var strip in _strips)
        {
            if (!strip.IsDestroyed)
            {
                strip.AcquireFenceFd = -1;
                strip.SetBuffer(null);
            }
        }

        if (_hasPending)
        {
            _pending.Dispose();
            _hasPending = false;
        }

        _shown.Dispose();
        _shown = default;
        _shownValid = false;

        if (_surface is not null)
        {
            _surface.RemoveObserver(this);
            try
            {
                _surface.Dispose();
            }
            catch (Exception e)
            {
                SceneLog.Log.Warn(e, $"frame: UI surface dispose failed at teardown");
            }

            _surface = null;
        }

        if (!_tree.IsDestroyed)
        {
            _tree.Destroy();
        }
    }
}
