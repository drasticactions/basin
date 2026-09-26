using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Paper;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private UISurfaceIndex? _uiIndex;
    private UISurfaceRouter? _uiRouter;
    private readonly ChordCapture _chordCapture = new();

    internal UISurfaceIndex UIIndex => _uiIndex ??= new UISurfaceIndex();

    internal UISurfaceRouter UIRouter => _uiRouter ??= CreateUIRouter();

    private UISurfaceRouter CreateUIRouter()
    {
        var router = new UISurfaceRouter(_scene, UIIndex) { ImplicitGrab = true };
        _textInput.CommitStringUnclaimed += text => router.TextCommit(text);
        return router;
    }

    private bool RouteUIMotion(uint time, double x, double y, bool refresh = false)
    {
        if (_uiRouter is not { } router || (_uiIndex!.Count == 0 && router.Hovered is null && router.PointerGrab is null))
        {
            return false;
        }

        if (!refresh && DragSettings(x, y))
        {
            router.PointerMotion(time, x, y);
            _seat.Pointer.NotifyMotionAt(time, null, 0, 0, x, y);
            return true;
        }

        if (refresh && router.PointerGrab is null && router.Hovered is { } hovered &&
            router.SurfaceAt(x, y) is { } still && ReferenceEquals(still.Surface, hovered))
        {
            return true;
        }

        var route = router.PointerMotion(time, x, y);
        if (route.Surface is null)
        {
            return false;
        }

        LeaveFrameHover(except: null);
        _cursor.SetHover(null, overClient: false);
        _cursor.ShowNamed(route.Cursor ?? "left_ptr");
        _seat.Pointer.NotifyMotionAt(time, null, 0, 0, x, y);
        return true;
    }

    private bool RouteUIButton(uint time, uint button, bool pressed)
    {
        if (_uiRouter is not { } router || (router.PointerGrab ?? router.Hovered) is not { } surface)
        {
            return false;
        }

        if (pressed && surface.AcceptsInput)
        {
            FocusUISurface(surface);
        }

        router.PointerButton(time, button, pressed);
        if (!pressed && router.PointerGrab is null)
        {
            EndSettingsDrag();
        }

        return true;
    }

    internal void HandleAxis(uint time, PointerAxis axis)
    {
        if (!RouteUIAxis(time, axis))
        {
            _seat.Pointer.NotifyAxis(time, axis);
        }
    }

    private bool RouteUIAxis(uint time, PointerAxis axis)
    {
        if (_uiRouter is not { } router || (router.PointerGrab ?? router.Hovered) is null)
        {
            return false;
        }

        var horizontal = axis.Axis == Wayland.WlPointer.Axis.HorizontalScroll;
        return router.PointerAxis(time, horizontal ? axis.Value : 0, horizontal ? 0 : axis.Value);
    }

    private bool RouteUIKey(uint time, uint key, bool pressed)
    {
        if (_uiRouter is not { KeyboardFocus: not null } router || _sessionLock.IsLocked || _effects.SwitcherActive)
        {
            return false;
        }

        if (_chordCapture.Active)
        {
            if (pressed)
            {
                var symbol = _seat.Keyboard.RawKeysymFor(key).Value;
                if (key == InputCodes.KeyEsc)
                {
                    _chordCapture.Cancel();
                }
                else if (symbol != Basin.Config.Keysym.NoSymbol && !IsModifierKeysym(symbol))
                {
                    _chordCapture.Offer(Basin.Config.HotkeyParser.Format(symbol, HeldModifiers()));
                }
            }

            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
            return true;
        }

        if (pressed && PassesThroughUI(key))
        {
            return false;
        }

        if (pressed && key == InputCodes.KeyEsc && _settingsSurface is { } panel &&
            ReferenceEquals(router.KeyboardFocus, panel) && !panel.WantsTextInput)
        {
            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
            CloseSettings();
            return true;
        }

        router.Key(time, key, pressed);
        _seat.Keyboard.NotifyKeyConsumed(key, pressed);
        return true;
    }

    private bool PassesThroughUI(uint key)
    {
        var symbol = _seat.Keyboard.RawKeysymFor(key).Value;
        var held = HeldModifiers();
        foreach (var binding in _config.Bindings)
        {
            if (binding.Keysym == symbol && binding.ModifierMask == held && binding.Action is { } action)
            {
                return PassesThroughUI(action);
            }
        }

        return false;
    }

    private static bool PassesThroughUI(KeyAction action) => action is KeyAction.Quit or KeyAction.Settings;

    private static bool IsModifierKeysym(uint keysym) =>
        ModifierOf(keysym) != Basin.Config.Modifiers.None || keysym is 0xffe5 or 0xffe6 or 0xff7f or 0xfe03 or 0xfe11 or 0xff7e;

    internal void FocusUISurface(IUISurface surface)
    {
        if (ReferenceEquals(UIRouter.KeyboardFocus, surface))
        {
            return;
        }

        DismissOpenMenu();
        _seat.Keyboard.NotifyClearFocus();
        _textInput.NotifyFocus(null);
        UIRouter.SetKeyboardFocus(surface);
    }

    internal void ReleaseUIKeyboard(bool restore)
    {
        if (_uiRouter is not { KeyboardFocus: not null } router)
        {
            return;
        }

        _chordCapture.Cancel();
        router.SetKeyboardFocus(null);
        if (!restore)
        {
            return;
        }

        if (_focused is { } window)
        {
            _seat.Keyboard.NotifyEnter(window.Toplevel.Surface);
            _textInput.NotifyFocus(window.Toplevel.Surface);
        }
        else if (_focusedX is { XWin.Surface: { } surface })
        {
            _seat.Keyboard.NotifyEnter(surface);
        }
    }
}
