using System.Runtime.InteropServices;
using Basin.Shell.River.Protocol;
using Wayland.Server;
using Xkb;

namespace Basin.Shell.River;

internal sealed class RiverXkbKeyboard : IRiverXkbKeyboardV1Handler
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

    internal RiverXkbKeyboardV1Resource Bind(WlClient client, uint version, uint id)
    {
        var resource = new RiverXkbKeyboardV1Resource(client, version, id, this);
        _resource = resource;
        _layoutSent = false;
        _locksSent = false;
        return resource;
    }

    void IRiverXkbKeyboardV1Handler.SetKeymap(RiverXkbKeyboardV1Resource resource, in RiverXkbKeyboardV1Resource.SetKeymapEventArgs args)
    {
        if (_owner.KeymapTextOf(args.Keymap) is not { } text)
        {
            resource.PostError(
                (uint)RiverXkbKeyboardV1.Error.InvalidKeymap,
                "The compositor did not validate this keymap.");
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
    }

    void IRiverXkbKeyboardV1Handler.SetLayoutByIndex(RiverXkbKeyboardV1Resource resource, in RiverXkbKeyboardV1Resource.SetLayoutByIndexEventArgs args)
    {
        if (args.Index >= 0)
        {
            ApplyLayout((uint)args.Index);
        }
    }

    void IRiverXkbKeyboardV1Handler.SetLayoutByName(RiverXkbKeyboardV1Resource resource, in RiverXkbKeyboardV1Resource.SetLayoutByNameEventArgs args)
    {
        if (args.Name is null)
        {
            return;
        }

        if (Seat.Keyboard.Keymap?.GetLayoutIndex(args.Name) is { } index)
        {
            ApplyLayout(index);
        }
    }

    void IRiverXkbKeyboardV1Handler.CapslockEnable(RiverXkbKeyboardV1Resource resource) => SetLock(ref _capsLock, true);

    void IRiverXkbKeyboardV1Handler.CapslockDisable(RiverXkbKeyboardV1Resource resource) => SetLock(ref _capsLock, false);

    void IRiverXkbKeyboardV1Handler.NumlockEnable(RiverXkbKeyboardV1Resource resource) => SetLock(ref _numLock, true);

    void IRiverXkbKeyboardV1Handler.NumlockDisable(RiverXkbKeyboardV1Resource resource) => SetLock(ref _numLock, false);

    void IRiverXkbKeyboardV1Handler.Destroy(RiverXkbKeyboardV1Resource resource) => _resource = null;

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
