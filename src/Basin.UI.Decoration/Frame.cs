using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Pixman;

namespace Basin.UI.Decoration;

public sealed class Frame : IUISurfaceObserver, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IUIHost _host;
    private readonly IFrameRenderer _renderer;
    private readonly SceneTree _tree;
    private readonly SceneBuffer[] _strips;
    private readonly Box[] _stripBoxes;
    private readonly PixmanRegion32 _scratch = new();
    private readonly PixmanRegion32 _interactionDamage = new();

    private IUISurface? _surface;
    private UIFrame _shown;
    private UIFrame _pending;
    private bool _hasPending;
    private bool _disposed;
    private bool _faulted;

    private Box _pendingGeometry;
    private FrameInsets _pendingInsets;
    private FrameState _pendingState;
    private double _pendingScale;

    private Box _shownGeometry;
    private FrameInsets _shownInsets;
    private FrameState _shownState;
    private double _shownScale;
    private bool _shownValid;

    private FrameInteraction _interaction;
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

    public Frame(IUIHost host, IFrameRenderer renderer, SceneTree parent)
    {
        _host = host;
        _renderer = renderer;
        _tree = new SceneTree(parent);
        _strips = new SceneBuffer[4];
        _stripBoxes = new Box[4];
        for (var i = 0; i < 4; i++)
        {
            _strips[i] = new SceneBuffer(_tree) { Enabled = false, PreciseDamage = true };
        }
    }

    public FrameInsets Insets { get; private set; }

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

    public void Configure(in Box geometry, double scale, in FrameState state)
    {
        _thread.Assert();
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
        try
        {
            insets = _renderer.Measure(state, scale);
        }
        catch (Exception e)
        {
            Fault(e);
            return;
        }

        var outerWidth = geometry.Width + insets.Left + insets.Right;
        var outerHeight = geometry.Height + insets.Top + insets.Bottom;

        try
        {
            if (_surface is null)
            {
                var target = (_host.Produces & UITargetKind.Dmabuf) != 0
                    ? UITargetKind.Dmabuf
                    : UITargetKind.Memory;
                _surface = _host.CreateSurface(new UISurfaceOptions
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

            var clientBox = new Box(insets.Left, insets.Top, geometry.Width, geometry.Height);
            _renderer.Draw(_surface, clientBox, state, _interaction);

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

        var buffer = _pending.Buffer;
        if (buffer is null)
        {
            _hasPending = false;
            return;
        }

        var insets = _pendingInsets;
        var geometry = _pendingGeometry;
        var outer = new Box(
            geometry.X - insets.Left,
            geometry.Y - insets.Top,
            geometry.Width + insets.Left + insets.Right,
            geometry.Height + insets.Top + insets.Bottom);

        _tree.SetPosition(outer.X, outer.Y);

        _stripBoxes[0] = new Box(0, 0, outer.Width, insets.Top);
        _stripBoxes[1] = new Box(0, insets.Top, insets.Left, geometry.Height);
        _stripBoxes[2] = new Box(outer.Width - insets.Right, insets.Top, insets.Right, geometry.Height);
        _stripBoxes[3] = new Box(0, outer.Height - insets.Bottom, outer.Width, insets.Bottom);

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
            strip.SourceBox = OutputScaling.ToPhysical(box, _pendingScale);
            strip.DestinationWidth = box.Width;
            strip.DestinationHeight = box.Height;
            strip.SetPosition(box.X, box.Y);
            strip.Enabled = true;
            strip.NotifyContentChanged();
        }

        _shown.Dispose();
        _shown = _pending;
        _pending = default;
        _hasPending = false;

        _shownGeometry = geometry;
        _shownInsets = insets;
        _shownState = _pendingState;
        _shownScale = _pendingScale;
        _shownValid = true;
        Insets = insets;
        OuterBounds = outer;

        DismissMenu();
        Committed?.Invoke();
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

        var sx = x - (_shownGeometry.X - _shownInsets.Left);
        var sy = y - (_shownGeometry.Y - _shownInsets.Top);
        var outerWidth = _shownGeometry.Width + _shownInsets.Left + _shownInsets.Right;
        var outerHeight = _shownGeometry.Height + _shownInsets.Top + _shownInsets.Bottom;
        if (sx < 0 || sy < 0 || sx >= outerWidth || sy >= outerHeight)
        {
            return FramePart.None;
        }

        if (sx >= _shownInsets.Left && sx < _shownInsets.Left + _shownGeometry.Width &&
            sy >= _shownInsets.Top && sy < _shownInsets.Top + _shownGeometry.Height)
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
        if (part != _interaction.Hot)
        {
            var previous = _interaction.Hot;
            _interaction = _interaction with { Hot = part };
            RedrawShown(previous, part);
        }
    }

    public void PointerLeave()
    {
        _thread.Assert();
        if (_interaction != default)
        {
            var previous = _interaction;
            _interaction = default;
            RedrawShown(previous.Hot, previous.Pressed);
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
                case FramePart.Menu:
                case FramePart.Icon:
                    _titleArmed = false;
                    if (_interaction.Pressed != part)
                    {
                        _interaction = _interaction with { Pressed = part };
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

        _interaction = _interaction with { Pressed = FramePart.None };
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
            case FramePart.Menu:
            case FramePart.Icon:
                _titleArmed = false;
                _touchLatch = id;
                _interaction = _interaction with { Pressed = part };
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

        _interaction = _interaction with { Pressed = FramePart.None };
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

        _interaction = _interaction with { Pressed = FramePart.None };
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
        TearDown();
        _scratch.Dispose();
        _interactionDamage.Dispose();
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

            var clientBox = new Box(
                _shownInsets.Left,
                _shownInsets.Top,
                _shownGeometry.Width,
                _shownGeometry.Height);
            _suppressSurfaceDamage = true;
            try
            {
                _renderer.Draw(_surface, clientBox, _shownState, _interaction);
            }
            finally
            {
                _suppressSurfaceDamage = false;
            }

            DamageStrips(_interactionDamage);
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
        DismissMenu();
        foreach (var strip in _strips)
        {
            if (!strip.IsDestroyed)
            {
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
