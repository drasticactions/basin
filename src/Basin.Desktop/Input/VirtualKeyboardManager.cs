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
public sealed class VirtualKeyboardManager : IDisposable, IZwpVirtualKeyboardManagerV1Handler
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

    void IZwpVirtualKeyboardManagerV1Handler.CreateVirtualKeyboard(ZwpVirtualKeyboardManagerV1Resource resource, in ZwpVirtualKeyboardManagerV1Resource.CreateVirtualKeyboardEventArgs args)
    {
        var client = resource.Client;
        var handler = new VirtualKeyboard(this);
        var keyboard = new ZwpVirtualKeyboardV1Resource(client, resource.Version, args.Id, handler);
        var seat = Seat.Seat.FromResource(args.Seat);
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
        handler.Sink = sink;
        handler.Device = device;
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwpVirtualKeyboardManagerV1Resource(client, version, id, this);

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

    private sealed class VirtualKeyboard(VirtualKeyboardManager owner) : IZwpVirtualKeyboardV1Handler
    {
        public IInputSink? Sink { get; set; }

        public IInjectedKeyboard? Device { get; set; }

        void IZwpVirtualKeyboardV1Handler.Keymap(ZwpVirtualKeyboardV1Resource resource, in ZwpVirtualKeyboardV1Resource.KeymapEventArgs args)
        {
            if (Device is not null && ReadKeymap(args.Fd, args.Size) is { } bytes)
            {
                Device.SetKeymap(bytes);
            }

            if (owner.KeymapSubmitted is { } handler)
            {
                handler(new ClientFd(args.Fd, resource.Client), args.Size);
            }
            else
            {
                resource.Client.CloseFd(args.Fd);
            }
        }

        void IZwpVirtualKeyboardV1Handler.Key(ZwpVirtualKeyboardV1Resource resource, in ZwpVirtualKeyboardV1Resource.KeyEventArgs args) =>
            Sink?.Key(Device, args.Time, args.Key, args.State == 1);

        void IZwpVirtualKeyboardV1Handler.Modifiers(ZwpVirtualKeyboardV1Resource resource, in ZwpVirtualKeyboardV1Resource.ModifiersEventArgs args) =>
            Sink?.Modifiers(Device, args.ModsDepressed, args.ModsLatched, args.ModsLocked, args.Group);

        void IZwpVirtualKeyboardV1Handler.Destroy(ZwpVirtualKeyboardV1Resource resource)
        {
        }
    }
}
