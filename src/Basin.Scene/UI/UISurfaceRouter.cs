using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

public sealed class UISurfaceRouter : IUISurfaceObserver
{
    private readonly Scene _scene;
    private readonly UISurfaceIndex _index;
    private IUISurface? _hovered;
    private IUISurface? _focus;

    public UISurfaceRouter(Scene scene, UISurfaceIndex index)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(index);

        _scene = scene;
        _index = index;
    }

    public IUISurface? Hovered => _hovered;

    public IUISurface? KeyboardFocus => _focus;

    public bool WantsTextInput => _focus?.WantsTextInput ?? false;

    public event Action<IUISurface?>? HoverChanged;

    public event Action<IUISurface?>? FocusChanged;

    public UISurfaceHit? SurfaceAt(double x, double y)
    {
        if (_scene.NodeAt(x, y) is not { } hit ||
            hit.Node is not SceneBuffer node ||
            _index.SurfaceOf(node) is not { } surface)
        {
            return null;
        }

        return new UISurfaceHit(surface, node, hit.X, hit.Y);
    }

    public string? CursorAt(double x, double y) =>
        SurfaceAt(x, y) is { } hit ? hit.Surface.CursorAt(hit.X, hit.Y) : null;

    public bool TryLocal(IUISurface surface, double x, double y, out double localX, out double localY)
    {
        localX = x;
        localY = y;
        if (surface is null || _index.OwnerOf(surface) is not { } owner)
        {
            return false;
        }

        return owner.Node.TryMapSceneToLocal(x, y, out localX, out localY);
    }

    public UIPointerRoute PointerMotion(uint timeMs, double x, double y)
    {
        if (SurfaceAt(x, y) is not { } hit)
        {
            PointerLeave();
            return default;
        }

        var entered = !ReferenceEquals(hit.Surface, _hovered);
        if (entered)
        {
            LeaveHovered();
            _hovered = hit.Surface;
            _hovered.AddObserver(this);
            _hovered.NotifyPointerEnter(hit.X, hit.Y);
            HoverChanged?.Invoke(_hovered);
        }
        else
        {
            hit.Surface.NotifyPointerMotion(timeMs, hit.X, hit.Y);
        }

        return new UIPointerRoute(hit.Surface, entered, hit.Surface.CursorAt(hit.X, hit.Y));
    }

    public void PointerLeave()
    {
        if (_hovered is null)
        {
            return;
        }

        LeaveHovered();
        HoverChanged?.Invoke(null);
    }

    public bool PointerButton(uint timeMs, uint button, bool pressed, IUISurface? target = null)
    {
        var surface = target ?? _hovered;
        if (surface is null)
        {
            return false;
        }

        surface.NotifyPointerButton(timeMs, button, pressed);
        return true;
    }

    public bool PointerAxis(uint timeMs, double dx, double dy, IUISurface? target = null)
    {
        var surface = target ?? _hovered;
        if (surface is null)
        {
            return false;
        }

        surface.NotifyPointerAxis(timeMs, dx, dy);
        return true;
    }

    public bool TouchDown(uint timeMs, int id, double x, double y)
    {
        if (_hovered is not { } surface)
        {
            return false;
        }

        TryLocal(surface, x, y, out var localX, out var localY);
        surface.NotifyTouchDown(timeMs, id, localX, localY);
        return true;
    }

    public bool TouchMotion(uint timeMs, int id, double x, double y)
    {
        if (_hovered is not { } surface)
        {
            return false;
        }

        TryLocal(surface, x, y, out var localX, out var localY);
        surface.NotifyTouchMotion(timeMs, id, localX, localY);
        return true;
    }

    public bool TouchUp(uint timeMs, int id)
    {
        if (_hovered is not { } surface)
        {
            return false;
        }

        surface.NotifyTouchUp(timeMs, id);
        return true;
    }

    public bool TouchCancel()
    {
        if (_hovered is not { } surface)
        {
            return false;
        }

        surface.NotifyTouchCancel();
        return true;
    }

    public void SetKeyboardFocus(IUISurface? surface, ReadOnlySpan<uint> pressed = default)
    {
        if (ReferenceEquals(surface, _focus))
        {
            return;
        }

        if (_focus is { } previous)
        {
            previous.NotifyKeyboardLeave();
            Release(previous);
        }

        _focus = surface;
        if (_focus is { } next)
        {
            next.AddObserver(this);
            next.NotifyKeyboardEnter(pressed);
        }

        FocusChanged?.Invoke(_focus);
    }

    public bool Key(uint timeMs, uint key, bool pressed)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyKey(timeMs, key, pressed);
        return true;
    }

    public bool Modifiers(uint depressed, uint latched, uint locked, uint group)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyModifiers(depressed, latched, locked, group);
        return true;
    }

    public bool TextCommit(ReadOnlySpan<char> text)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyTextCommit(text);
        return true;
    }

    public bool Preedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyPreedit(text, cursorBegin, cursorEnd);
        return true;
    }

    public void Forget(IUISurface surface)
    {
        if (ReferenceEquals(surface, _hovered))
        {
            _hovered = null;
            Release(surface);
            HoverChanged?.Invoke(null);
        }

        if (ReferenceEquals(surface, _focus))
        {
            _focus = null;
            Release(surface);
            FocusChanged?.Invoke(null);
        }
    }

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    public void OnSurfaceDestroyed(IUISurface surface) => Forget(surface);

    private void LeaveHovered()
    {
        if (_hovered is not { } surface)
        {
            return;
        }

        _hovered = null;
        surface.NotifyPointerLeave();
        Release(surface);
    }

    private void Release(IUISurface surface)
    {
        if (!ReferenceEquals(surface, _hovered) && !ReferenceEquals(surface, _focus))
        {
            surface.RemoveObserver(this);
        }
    }
}
