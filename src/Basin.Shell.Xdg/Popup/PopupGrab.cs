using Basin.Seat;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

internal sealed class PopupGrab : IPointerGrab, IKeyboardGrab, ITouchGrab
{
    private static readonly Dictionary<Basin.Seat.Seat, PopupGrab> Active = [];

    private readonly Basin.Seat.Seat _seat;
    private readonly List<XdgPopupWindow> _popups = [];
    private Surface? _previousKeyboardFocus;

    private PopupGrab(Basin.Seat.Seat seat)
    {
        _seat = seat;
    }

    public static PopupGrab GetOrCreate(Basin.Seat.Seat seat)
    {
        if (!Active.TryGetValue(seat, out var grab))
        {
            grab = new PopupGrab(seat);
            Active[seat] = grab;
            grab._previousKeyboardFocus = seat.Keyboard.Focus;
            seat.Pointer.StartGrab(grab);
            seat.Keyboard.StartGrab(grab);
            seat.Touch.StartGrab(grab);
        }

        return grab;
    }

    public void Add(XdgPopupWindow popup)
    {
        _popups.Add(popup);
        _seat.Keyboard.SendEnter(popup.Surface, _seat.Keyboard.PressedKeys.ToArray());
    }

    public void Remove(XdgPopupWindow popup)
    {
        _popups.Remove(popup);
        if (_popups.Count == 0)
        {
            End();
        }
        else
        {
            _seat.Keyboard.SendEnter(_popups[^1].Surface, _seat.Keyboard.PressedKeys.ToArray());
        }
    }

    public void DismissChainFrom(XdgPopupWindow popup)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var candidate = _popups[i];
            if (candidate == popup)
            {
                break;
            }

            candidate.Dismiss();
        }
    }

    public void Enter(Surface? surface, double x, double y) => _seat.Pointer.SendEnter(surface, x, y);

    public void Motion(uint timeMs, double x, double y) => _seat.Pointer.SendMotion(timeMs, x, y);

    public uint Button(uint timeMs, uint button, Wayland.WlPointer.ButtonState state)
    {
        var focus = _seat.Pointer.Focus;
        if (state == Wayland.WlPointer.ButtonState.Pressed && !IsOnGrabbingClient(focus))
        {
            DismissAll();
            return 0;
        }

        return _seat.Pointer.SendButton(timeMs, button, state);
    }

    public void Axis(uint timeMs, in Basin.PointerAxis axis) => _seat.Pointer.SendAxis(timeMs, axis);

    public uint Down(Surface surface, uint timeMs, int id, double x, double y)
    {
        if (!IsOnGrabbingClient(surface))
        {
            DismissAll();
            return 0;
        }

        return _seat.Touch.SendDown(surface, timeMs, id, x, y);
    }

    public void Up(uint timeMs, int id) => _seat.Touch.SendUp(timeMs, id);

    public void Motion(uint timeMs, int id, double x, double y) => _seat.Touch.SendMotion(timeMs, id, x, y);

    public void Frame() => _seat.Touch.SendFrame();

    void ITouchGrab.Cancel() => _seat.Touch.SendCancel();

    public void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys)
    {
        if (_popups.Count > 0)
        {
            _seat.Keyboard.SendEnter(_popups[^1].Surface, pressedKeys);
        }
    }

    public void Key(uint timeMs, uint key, Wayland.WlKeyboard.KeyState state) => _seat.Keyboard.SendKey(timeMs, key, state);

    public void Modifiers() => _seat.Keyboard.SendModifiers();

    public void Cancel()
    {
    }

    private bool IsOnGrabbingClient(Surface? surface)
    {
        if (surface is null || _popups.Count == 0)
        {
            return false;
        }

        return !surface.IsDestroyed && surface.Resource.Client == _popups[0].Surface.Resource.Client;
    }

    private void DismissAll()
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            _popups[i].Dismiss();
        }

        _popups.Clear();
        End();
    }

    private void End()
    {
        if (Active.TryGetValue(_seat, out var grab) && grab == this)
        {
            Active.Remove(_seat);
            _seat.Pointer.EndGrab(this);
            _seat.Keyboard.EndGrab(this);
            _seat.Touch.EndGrab(this);
            _seat.Keyboard.SendEnter(_previousKeyboardFocus, _seat.Keyboard.PressedKeys.ToArray());
        }
    }
}
