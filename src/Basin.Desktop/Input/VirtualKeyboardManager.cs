using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_virtual_keyboard_manager_v1</c> global. Each virtual keyboard becomes an injected keyboard on the input sink of its seat, or of the default sink.
/// </summary>
public sealed class VirtualKeyboardManager : IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private const int MaxKeymapBytes = 8 * 1024 * 1024;

    private readonly WlGlobal _global;
    private readonly IInputSink? _sink;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualKeyboardManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="sink">The default sink. When it is <see langword="null"/>, a keyboard with no seat has no effect.</param>
    public VirtualKeyboardManager(WlServerDisplay display, IInputSink? sink)
    {
        ArgumentNullException.ThrowIfNull(display);
        _sink = sink;
        _global = display.CreateGlobal(ZwpVirtualKeyboardManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client sends a keymap. The arguments are the keymap fd and its size in bytes. The handler owns the fd and must close it. Without a handler, the manager closes it.
    /// </summary>
    public event Action<ClientFd, uint>? KeymapSubmitted;

    /// <summary>
    /// Removes the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpVirtualKeyboardManagerV1Resource(client, version, id);
        manager.CreateVirtualKeyboard += (_, e) =>
        {
            var keyboard = new ZwpVirtualKeyboardV1Resource(client, manager.Version, e.Id);
            var seat = Seat.Seat.FromResource(e.Seat);
            var sink = seat?.InputSink ?? _sink;
            var device = sink?.CreateKeyboard();
            if (device is not null)
            {
                device.Tag = client;
            }

            seat?.AddVirtualDevice(SeatCapability.Keyboard);
            keyboard.Destroyed += (_, _) =>
            {
                device?.Dispose();
                seat?.RemoveVirtualDevice(SeatCapability.Keyboard);
            };
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
            keyboard.Key += (_, ke) => sink?.Key(device, ke.Time, ke.Key, ke.State == 1);
            keyboard.Modifiers += (_, me) =>
                sink?.Modifiers(device, me.ModsDepressed, me.ModsLatched, me.ModsLocked, me.Group);
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
