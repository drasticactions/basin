using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

public sealed class Frame : IUISurfaceObserver, ISceneInputFilter, IFrameSink, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly FrameHosts _hosts;
    private readonly IFrameRenderer _renderer;
    private readonly SceneTree _tree;
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

    public Frame(IUIHost host, IFrameRenderer renderer, SceneTree parent)
        : this(new FrameHosts(host), renderer, parent)
    {
    }

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

    public Surface? Window { get; set; }

    public long DrawCount { get; private set; }

    public FrameState DrawnState { get; private set; }

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

    public SceneTree Tree => _tree;

    public IBuffer? PresentedChrome => _shown.Buffer;

    public FrameInsets Insets { get; private set; }

    public FrameInsets Outsets { get; private set; }

    public Box DrawnBounds { get; private set; }

    public IFrameClock? Clock { get; set; }

    public Func<long> Time { get; set; } = static () => MonotonicClock.Nanos;

    public bool Animating => _animating;

    public IReadOnlyList<SceneBuffer> StripNodes => _strips;

    public Box StripBounds(int index) => _stripBoxes[index];

    public Box OuterBounds { get; private set; }

    public double Scale => _shownScale;

    public event Action? Committed;

    public bool IsFaulted => _faulted;

    public bool Visible
    {
        get => _tree.Enabled;
        set => _tree.Enabled = value;
    }

    public event Action<FrameAction>? Requested;

    public IBackdropEffect? BackdropEffect { get; set; }

    public object? BackdropKey { get; set; }

    public IBackdropEffect? UnderlayEffect { get; set; }

    public object? UnderlayKey { get; set; }

    public bool UnderlayCoversClient { get; set; } = true;

    public SceneBuffer UnderlayNode => _underlay;

    public event Action<FrameBackdropShape>? ShapePublished;

    public event Action<FrameState, FrameInteraction>? Drawn;

    public SceneTree? MenuLayer { get; set; }

    public Box MenuConstraint { get; set; }

    public bool IsMenuOpen => _menuSurface is not null;

    public event Action<Exception>? Faulted;

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

    public bool HasPendingFor(in Box geometry, double scale)
    {
        _thread.Assert();
        return _hasPending && _pendingGeometry == geometry && _pendingScale == scale;
    }

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

    public void PointerEnter(double x, double y) => PointerMotion(x, y);

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

    public double TouchSlop { get; set; }

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

    public Point MenuOrigin { get; set; }

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

    public void DismissMenu()
    {
        _thread.Assert();
        if (_menuSurface is null)
        {
            return;
        }

        var popup = _menuSurface;
        _menuSurface = null;
        popup.RemoveObserver(this);
        _menuNode?.Destroy();
        _menuNode = null;
        _menuShown.Dispose();
        _menuShown = default;
        _menuHotItem = -1;
        try
        {
            popup.Dispose();
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    public bool OwnsMenuNode(SceneNode node) => _menuNode is not null && node == _menuNode;

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

    public void OnSurfaceDestroyed(IUISurface surface)
    {
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

    public void BeginFrame(IOutput output, long predictedVblankNanos) => Tick(predictedVblankNanos);

    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

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
            catch
            {
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
            catch
            {
            }

            _surface = null;
        }

        if (!_tree.IsDestroyed)
        {
            _tree.Destroy();
        }
    }
}
