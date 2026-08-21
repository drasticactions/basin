using System.Runtime.InteropServices;
using Basin.Shell.River.Protocol;
using Wayland.Server;
using Xkb;

namespace Basin.Shell.River;

internal sealed class RiverXkbKeyboard
{
    private readonly RiverXkbConfig _owner;
    private RiverXkbKeyboardV1Resource? _resource;
    private uint _sentLayout;
    private string? _sentLayoutName;
    private bool _layoutSent;
    private bool _capsLock;
    private bool _numLock;
    private bool _locksSent;

    internal RiverXkbKeyboard(RiverXkbConfig owner, object handle, Basin.Seat.Seat seat, Basin.Capabilities.IInjectedKeyboard? device)
    {
        _owner = owner;
        Handle = handle;
        Seat = seat;
        Device = device;
    }

    internal object Handle { get; }

    internal Basin.Seat.Seat Seat { get; }

    internal Basin.Capabilities.IInjectedKeyboard? Device { get; }

    internal void Bind(RiverXkbKeyboardV1Resource resource)
    {
        _resource = resource;
        _layoutSent = false;
        _locksSent = false;

        resource.SetKeymap += (_, e) =>
        {
            if (_owner.KeymapTextOf(e.Keymap) is not { } text)
            {
                resource.PostError(
                    (uint)RiverXkbKeyboardV1.Error.InvalidKeymap,
                    "the keymap was never validated by this compositor");
                return;
            }

            if (Device is { } device)
            {
                device.SetKeymap(System.Text.Encoding.UTF8.GetBytes(text));
            }
            else
            {
                Seat.Keyboard.SetKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(text));
            }

            _capsLock = false;
            _numLock = false;
            _layoutSent = false;
            _locksSent = false;
            _owner.Manager.MarkManageDirty();
        };

        resource.SetLayoutByIndex += (_, e) =>
        {
            if (e.Index >= 0)
            {
                ApplyLayout((uint)e.Index);
            }
        };

        resource.SetLayoutByName += (_, e) =>
        {
            if (e.Name is null)
            {
                return;
            }

            if (Seat.Keyboard.Keymap?.GetLayoutIndex(e.Name) is { } index)
            {
                ApplyLayout(index);
            }
        };

        resource.CapslockEnable += (_, _) => SetLock(ref _capsLock, true);
        resource.CapslockDisable += (_, _) => SetLock(ref _capsLock, false);
        resource.NumlockEnable += (_, _) => SetLock(ref _numLock, true);
        resource.NumlockDisable += (_, _) => SetLock(ref _numLock, false);
        resource.DestroyRequest += (_, _) => _resource = null;
    }

    internal void SendState(uint version)
    {
        if (_resource is not { IsDestroyed: false } resource)
        {
            return;
        }

        var keymap = Seat.Keyboard.Keymap;
        var layout = Seat.Keyboard.State?.SerializeLayout(XkbStateComponent.LayoutEffective) ?? 0;
        var name = keymap?.GetLayoutName(layout);
        var changed = false;

        if (!_layoutSent || _sentLayout != layout || _sentLayoutName != name)
        {
            _layoutSent = true;
            _sentLayout = layout;
            _sentLayoutName = name;
            resource.SendLayout(layout, name);
            changed = true;
        }

        if (!_locksSent)
        {
            _locksSent = true;
            changed = true;
            if (_capsLock)
            {
                resource.SendCapslockEnabled();
            }
            else
            {
                resource.SendCapslockDisabled();
            }

            if (_numLock)
            {
                resource.SendNumlockEnabled();
            }
            else
            {
                resource.SendNumlockDisabled();
            }
        }

        if (changed && version >= 2)
        {
            resource.SendDone();
        }
    }

    internal void SendRemoved()
    {
        if (_resource is { IsDestroyed: false } resource)
        {
            resource.SendRemoved();
        }

        _resource = null;
    }

    internal void ResetForNewManager()
    {
        _resource = null;
        _layoutSent = false;
        _locksSent = false;
    }

    private void ApplyLayout(uint index)
    {
        if (Seat.Keyboard.Keymap is not { } keymap || index >= keymap.LayoutCount)
        {
            return;
        }

        var state = Seat.Keyboard.State;
        if (state is null)
        {
            return;
        }

        var (depressed, latched, locked, _) = Seat.Keyboard.ModifierState;
        state.UpdateMask(depressed, latched, locked, 0, 0, index);
        Seat.Keyboard.NotifyModifiers(depressed, latched, locked, index);
        _owner.Manager.MarkManageDirty();
    }

    private void SetLock(ref bool flag, bool enabled)
    {
        if (flag == enabled)
        {
            return;
        }

        flag = enabled;
        _locksSent = false;
        _owner.Manager.MarkManageDirty();
    }
}
