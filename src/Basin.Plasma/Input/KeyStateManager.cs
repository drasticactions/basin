using Basin.Plasma.Protocol;
using Basin.Seat;
using Wayland.Server;
using Xkb;

namespace Basin.Plasma;

public sealed class KeyStateManager : IDisposable
{
    public const int Version = 5;

    private readonly WlGlobal _global;
    private readonly SeatKeyboard? _keyboard;
    private readonly List<OrgKdeKwinKeystateResource> _resources = [];
    private uint? _caps;
    private uint? _num;
    private uint? _alt;
    private uint? _control;
    private uint? _shift;
    private uint? _meta;
    private uint? _altgr;
    private ((uint Depressed, uint Latched, uint Locked, uint Group) Modifiers, bool ScrollLock)? _sent;

    public KeyStateManager(WlServerDisplay display, Seat.Seat? seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        _keyboard = seat?.Keyboard;
        ResolveIndices();
        _global = display.CreateGlobal(OrgKdeKwinKeystate.Interface, Version, OnBind);
        if (_keyboard is { } keyboard)
        {
            keyboard.ModifiersChanged += OnModifiersChanged;
            keyboard.LedsChanged += OnModifiersChanged;
            keyboard.KeymapChanged += OnKeymapChanged;
        }
    }

    public void Dispose()
    {
        if (_keyboard is { } keyboard)
        {
            keyboard.ModifiersChanged -= OnModifiersChanged;
            keyboard.LedsChanged -= OnModifiersChanged;
            keyboard.KeymapChanged -= OnKeymapChanged;
        }

        _resources.Clear();
        _global.Dispose();
    }

    private void OnModifiersChanged()
    {
        if (_keyboard is { } keyboard)
        {
            var now = (keyboard.ModifierState, (keyboard.Leds & KeyboardLeds.ScrollLock) != 0);
            if (_sent == now)
            {
                return;
            }

            _sent = now;
        }

        foreach (var resource in _resources)
        {
            SendStates(resource);
        }
    }

    private void OnKeymapChanged()
    {
        ResolveIndices();
        _sent = null;
        OnModifiersChanged();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new OrgKdeKwinKeystateResource(client, version, id);
        _resources.Add(resource);
        resource.FetchStates += (_, _) => SendStates(resource);
        resource.Destroyed += (_, _) => _resources.Remove(resource);
    }

    private void SendStates(OrgKdeKwinKeystateResource resource)
    {
        if (resource.IsDestroyed)
        {
            return;
        }

        var pressed = resource.Version >= 5;
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Capslock, (uint)ModifierState(_caps, pressed));
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Numlock, (uint)ModifierState(_num, pressed));
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Scrolllock, (uint)ScrollLockState());
        if (resource.Version < 5)
        {
            return;
        }

        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Alt, (uint)ModifierState(_alt, pressed));
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Control, (uint)ModifierState(_control, pressed));
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Shift, (uint)ModifierState(_shift, pressed));
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Meta, (uint)ModifierState(_meta, pressed));
        resource.SendStateChanged((uint)OrgKdeKwinKeystate.Key.Altgr, (uint)ModifierState(_altgr, pressed));
    }

    private OrgKdeKwinKeystate.State ModifierState(uint? index, bool allowPressed)
    {
        if (_keyboard?.State is not { } state || index is not { } mod)
        {
            return OrgKdeKwinKeystate.State.Unlocked;
        }

        if (state.IsModActive(mod, XkbStateComponent.ModsLocked))
        {
            return OrgKdeKwinKeystate.State.Locked;
        }

        if (state.IsModActive(mod, XkbStateComponent.ModsLatched))
        {
            return OrgKdeKwinKeystate.State.Latched;
        }

        if (allowPressed && state.IsModActive(mod, XkbStateComponent.ModsDepressed))
        {
            return OrgKdeKwinKeystate.State.Pressed;
        }

        return OrgKdeKwinKeystate.State.Unlocked;
    }

    private OrgKdeKwinKeystate.State ScrollLockState() =>
        _keyboard is { } keyboard && (keyboard.Leds & KeyboardLeds.ScrollLock) != 0
            ? OrgKdeKwinKeystate.State.Locked
            : OrgKdeKwinKeystate.State.Unlocked;

    private void ResolveIndices()
    {
        var keymap = _keyboard?.Keymap;
        _caps = keymap?.GetModIndex(XkbNames.ModCaps);
        _num = keymap?.GetModIndex(XkbNames.ModMod2);
        _alt = keymap?.GetModIndex(XkbNames.ModMod1);
        _control = keymap?.GetModIndex(XkbNames.ModCtrl);
        _shift = keymap?.GetModIndex(XkbNames.ModShift);
        _meta = keymap?.GetModIndex(XkbNames.ModMod4);
        _altgr = keymap?.GetModIndex(XkbNames.ModMod5);
    }
}
