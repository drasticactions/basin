using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

public sealed class VirtualKeyboardManager : IDisposable
{
    public const int Version = 1;

    private const int MaxKeymapBytes = 8 * 1024 * 1024;

    private readonly WlGlobal _global;
    private readonly IInputSink? _sink;

    public VirtualKeyboardManager(WlServerDisplay display, IInputSink? sink)
    {
        ArgumentNullException.ThrowIfNull(display);
        _sink = sink;
        _global = display.CreateGlobal(ZwpVirtualKeyboardManagerV1.Interface, Version, OnBind);
    }

    public event Action<ClientFd, uint>? KeymapSubmitted;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpVirtualKeyboardManagerV1Resource(client, version, id);
        manager.CreateVirtualKeyboard += (_, e) =>
        {
            var keyboard = new ZwpVirtualKeyboardV1Resource(client, manager.Version, e.Id);
            var device = _sink?.CreateKeyboard();
            if (device is not null)
            {
                device.Tag = client;
            }

            keyboard.Destroyed += (_, _) => device?.Dispose();
            keyboard.Keymap += (_, ke) =>
            {
                if (device is not null && ReadKeymap(ke.Fd, ke.Size) is { } bytes)
                {
                    device.SetKeymap(bytes);
                }

                if (KeymapSubmitted is { } handler)
                {
                    handler(new ClientFd(ke.Fd, keyboard.Client), ke.Size);
                }
                else
                {
                    keyboard.Client.CloseFd(ke.Fd);
                }
            };
            keyboard.Key += (_, ke) => _sink?.Key(device, ke.Time, ke.Key, ke.State == 1);
            keyboard.Modifiers += (_, me) =>
                _sink?.Modifiers(device, me.ModsDepressed, me.ModsLatched, me.ModsLocked, me.Group);
        };
    }

    private static byte[]? ReadKeymap(int fd, uint size)
    {
        if (size == 0 || size > MaxKeymapBytes)
        {
            return null;
        }

        var bytes = new byte[size];
        try
        {
            using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: false);
            var read = System.IO.RandomAccess.Read(handle, bytes, 0);
            return read > 0 ? bytes.AsSpan(0, read).ToArray() : null;
        }
        catch (System.IO.IOException)
        {
            return null;
        }
    }
}
