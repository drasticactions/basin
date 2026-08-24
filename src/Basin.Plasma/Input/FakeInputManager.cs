using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class FakeInputManager : IDisposable
{
    public const int Version = 6;

    private const uint UnmappedKeyCode = 247;

    private readonly WlGlobal _global;
    private readonly IFakeInputAuthority? _authority;
    private readonly IInputSink? _sink;
    private readonly Basin.Seat.Seat? _seat;
    private readonly OutputLayout? _layout;
    private readonly List<FakeInputDevice> _devices = [];

    public FakeInputManager(
        WlServerDisplay display,
        IFakeInputAuthority? authority,
        IInputSink? sink,
        Basin.Seat.Seat? seat,
        OutputLayout? layout)
    {
        ArgumentNullException.ThrowIfNull(display);
        _authority = authority;
        _sink = sink;
        _seat = seat;
        _layout = layout;
        _global = display.CreateGlobal(OrgKdeKwinFakeInput.Interface, Version, OnBind);
    }

    public void Dispose()
    {
        foreach (var device in _devices.ToArray())
        {
            Teardown(device);
        }

        _devices.Clear();
        _global.Dispose();
    }

    private static uint Now => (uint)Environment.TickCount;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new OrgKdeKwinFakeInputResource(client, version, id);
        var device = new FakeInputDevice(resource);
        _devices.Add(device);

        resource.Authenticate += (_, e) => Authenticate(device, e.Application, e.Reason);
        resource.PointerMotion += (_, e) => PointerMotion(device, e.DeltaX.ToDouble(), e.DeltaY.ToDouble());
        resource.Button += (_, e) => Button(device, e.Button, e.State);
        resource.Axis += (_, e) => Axis(device, e.Axis, e.Value.ToDouble());
        resource.TouchDown += (_, e) => TouchDown(device, e.Id, e.X.ToDouble(), e.Y.ToDouble());
        resource.TouchMotion += (_, e) => TouchMotion(device, e.Id, e.X.ToDouble(), e.Y.ToDouble());
        resource.TouchUp += (_, e) => TouchUp(device, e.Id);
        resource.TouchCancel += (_, _) => TouchCancel(device);
        resource.TouchFrame += (_, _) => TouchFrame(device);
        resource.PointerMotionAbsolute += (_, e) => PointerMotionAbsolute(device, e.X.ToDouble(), e.Y.ToDouble());
        resource.KeyboardKey += (_, e) => KeyboardKey(device, e.Button, e.State);
        resource.KeyboardKeysym += (_, e) => KeyboardKeysym(device, e.Keysym, e.State);
        resource.Destroyed += (_, _) =>
        {
            if (_devices.Remove(device))
            {
                Teardown(device);
            }
        };
    }

    private void Authenticate(FakeInputDevice device, string application, string reason)
    {
        if (_authority is not { } authority)
        {
            device.Authorized = false;
            return;
        }

        var client = device.Resource.Client;
        uint pid = 0;
        uint uid = 0;
        uint gid = 0;
        if (client.TryGetCredentials(out var credentials))
        {
            pid = (uint)credentials.Pid;
            uid = (uint)credentials.Uid;
            gid = (uint)credentials.Gid;
        }

        var context = Basin.Desktop.SecurityContextManager.ContextOf(client);
        device.Authorized = authority.Authorize(new FakeInputRequest
        {
            Client = client,
            Application = application,
            Reason = reason,
            Pid = pid,
            Uid = uid,
            Gid = gid,
            SandboxAppId = context?.AppId,
            SandboxEngine = context?.SandboxEngine,
        });
    }

    private void PointerMotion(FakeInputDevice device, double dx, double dy)
    {
        if (!device.Authorized || _sink is not { } sink)
        {
            return;
        }

        sink.PointerMotion(Now, dx, dy);
        sink.Frame();
    }

    private void Button(FakeInputDevice device, uint button, uint state)
    {
        if (!device.Authorized || _sink is not { } sink)
        {
            return;
        }

        switch (state)
        {
            case 1:
                if (!device.PressedButtons.Add(button))
                {
                    return;
                }

                break;
            case 0:
                if (!device.PressedButtons.Remove(button))
                {
                    return;
                }

                break;
            default:
                return;
        }

        sink.PointerButton(Now, button, state == 1);
        sink.Frame();
    }

    private void Axis(FakeInputDevice device, uint axis, double value)
    {
        if (!device.Authorized || _sink is not { } sink || axis > 1)
        {
            return;
        }

        sink.PointerAxis(Now, axis, value);
        sink.Frame();
    }

    private void TouchDown(FakeInputDevice device, uint id, double x, double y)
    {
        if (!device.Authorized || _sink is not { } sink || FirstOutput() is not { } output)
        {
            return;
        }

        if (!device.ActiveTouches.Add(id))
        {
            return;
        }

        var (layoutX, layoutY) = _layout!.FromNormalized(output, x, y);
        var box = _layout.BoxOf(output);
        sink.TouchDown(Now, (int)id, layoutX, layoutY, box.Width, box.Height);
    }

    private void TouchMotion(FakeInputDevice device, uint id, double x, double y)
    {
        if (!device.Authorized || _sink is not { } sink || FirstOutput() is not { } output)
        {
            return;
        }

        if (!device.ActiveTouches.Contains(id))
        {
            return;
        }

        var (layoutX, layoutY) = _layout!.FromNormalized(output, x, y);
        var box = _layout.BoxOf(output);
        sink.TouchMotion(Now, (int)id, layoutX, layoutY, box.Width, box.Height);
    }

    private void TouchUp(FakeInputDevice device, uint id)
    {
        if (!device.Authorized || _sink is not { } sink)
        {
            return;
        }

        if (device.ActiveTouches.Remove(id))
        {
            sink.TouchUp(Now, (int)id);
        }
    }

    private void TouchCancel(FakeInputDevice device)
    {
        if (!device.Authorized || _sink is not { } sink)
        {
            return;
        }

        device.ActiveTouches.Clear();
        sink.TouchCancel();
    }

    private void TouchFrame(FakeInputDevice device)
    {
        if (!device.Authorized || _sink is not { } sink)
        {
            return;
        }

        sink.TouchFrame();
    }

    private void PointerMotionAbsolute(FakeInputDevice device, double x, double y)
    {
        if (!device.Authorized || _sink is not { } sink || _layout is not { } layout)
        {
            return;
        }

        var bounds = layout.Bounds;
        sink.PointerMotionAbsolute(Now, x, y, bounds.Width, bounds.Height);
        sink.Frame();
    }

    private void KeyboardKey(FakeInputDevice device, uint key, uint state)
    {
        if (!device.Authorized || state > 1)
        {
            return;
        }

        SendKey(device, key, state == 1);
    }

    private void KeyboardKeysym(FakeInputDevice device, uint keysym, uint state)
    {
        if (!device.Authorized || _sink is not { } sink || state > 1)
        {
            return;
        }

        if (_seat is not { } seat)
        {
            return;
        }

        var pressed = state == 1;
        var keyboard = seat.Keyboard;
        var former = keyboard.ModifierState;
        if (keyboard.TryKeycodeForKeysym(keysym, out var keycode, out var modifiers))
        {
            if (IsModifierKeysym(keysym))
            {
                SendKey(device, keycode, pressed);
            }
            else
            {
                sink.Modifiers(null, former.Depressed | modifiers, former.Latched, former.Locked, former.Group);
                SendKey(device, keycode, pressed);
                sink.Modifiers(null, former.Depressed, former.Latched, former.Locked, former.Group);
            }

            return;
        }

        if (!pressed)
        {
            return;
        }

        if (keyboard.OverrideKeymapForKeysym(UnmappedKeyCode, keysym) is not { } scope)
        {
            return;
        }

        try
        {
            SendKey(device, UnmappedKeyCode, pressed: true);
            foreach (var key in device.PressedKeys.ToArray())
            {
                SendKey(device, key, pressed: false);
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    private void SendKey(FakeInputDevice device, uint key, bool pressed)
    {
        if (_sink is not { } sink)
        {
            return;
        }

        if (pressed)
        {
            if (device.PressedKeys.Contains(key))
            {
                return;
            }

            device.PressedKeys.Add(key);
        }
        else if (!device.PressedKeys.Remove(key))
        {
            return;
        }

        sink.Key(null, Now, key, pressed);
    }

    private static bool IsModifierKeysym(uint keysym) =>
        keysym is >= 0xffe1 and <= 0xffe4 or >= 0xffe7 and <= 0xffec;

    private IOutput? FirstOutput() =>
        _layout is { Outputs.Count: > 0 } layout ? layout.Outputs[0].Output : null;

    private void Teardown(FakeInputDevice device)
    {
        if (_sink is { } sink && device.Authorized)
        {
            foreach (var button in device.PressedButtons)
            {
                sink.PointerButton(Now, button, pressed: false);
                sink.Frame();
            }

            foreach (var key in device.PressedKeys.ToArray())
            {
                sink.Key(null, Now, key, pressed: false);
            }

            if (device.ActiveTouches.Count > 0)
            {
                sink.TouchCancel();
            }
        }

        device.PressedButtons.Clear();
        device.PressedKeys.Clear();
        device.ActiveTouches.Clear();
        if (device.Authorized)
        {
            device.Authorized = false;
            _authority?.Revoked(device.Resource.Client);
        }
    }
}
