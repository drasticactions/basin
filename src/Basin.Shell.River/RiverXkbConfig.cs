using System.Runtime.InteropServices;
using Basin.Shell.River.Protocol;
using Wayland.Server;
using Xkb;

namespace Basin.Shell.River;

/// <summary>
/// The river-xkb-config-v1 global. It lets the window manager validate keymaps and set them on keyboards, and it reports the layout and lock state of each keyboard.
/// </summary>
public sealed class RiverXkbConfig : IDisposable
{
    private readonly RiverWindowManager _manager;
    private readonly WlGlobal _global;
    private readonly List<RiverXkbKeyboard> _keyboards = [];
    private RiverXkbConfigV1Resource? _resource;
    private bool _disposed;

    internal RiverXkbConfig(RiverWindowManager manager, WlServerDisplay display)
    {
        _manager = manager;
        _global = display.CreateGlobal(
            RiverXkbConfigV1.Interface,
            RiverXkbConfigV1.Interface.Version,
            OnBind);
    }

    /// <summary>
    /// Gets a value indicating whether a window manager holds a live binding to this global.
    /// </summary>
    public bool IsBound => _resource is { IsDestroyed: false };

    /// <summary>
    /// Registers a keyboard and announces it to the bound window manager. A handle that is already registered does nothing.
    /// </summary>
    /// <param name="handle">The consumer's object for the keyboard.</param>
    /// <param name="seat">The seat whose keyboard state the keyboard drives.</param>
    /// <param name="device">The device that receives a new keymap. When it is <see langword="null"/>, the keymap goes to the keyboard of <paramref name="seat"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> or <paramref name="seat"/> is <see langword="null"/>.</exception>
    public void AddKeyboard(object handle, Basin.Seat.Seat seat, Basin.Capabilities.IInjectedKeyboard? device = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(seat);
        if (_keyboards.Exists(k => ReferenceEquals(k.Handle, handle)))
        {
            return;
        }

        var keyboard = new RiverXkbKeyboard(this, handle, seat, device);
        _keyboards.Add(keyboard);
        Announce(keyboard);
    }

    /// <summary>
    /// Unregisters a keyboard and tells the window manager that it is gone. An unknown handle does nothing.
    /// </summary>
    /// <param name="handle">The handle given to <see cref="AddKeyboard"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    public void RemoveKeyboard(object handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (_keyboards.Find(k => ReferenceEquals(k.Handle, handle)) is { } keyboard)
        {
            _keyboards.Remove(keyboard);
            keyboard.SendRemoved();
        }
    }

    /// <summary>
    /// Forgets every keyboard and removes the global. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keyboards.Clear();
        _global.Dispose();
    }

    internal void ResetForNewManager()
    {
        _resource = null;
        foreach (var keyboard in _keyboards)
        {
            keyboard.ResetForNewManager();
        }
    }

    internal RiverWindowManager Manager => _manager;

    internal void SendKeyboardState(uint version)
    {
        foreach (var keyboard in _keyboards)
        {
            keyboard.SendState(version);
        }
    }

    private void Announce(RiverXkbKeyboard keyboard)
    {
        if (_resource is not { IsDestroyed: false } config)
        {
            return;
        }

        var resource = new RiverXkbKeyboardV1Resource(config.Client, config.Version, 0);
        keyboard.Bind(resource);
        config.SendXkbKeyboard(resource);
        keyboard.SendState(config.Version);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverXkbConfigV1Resource(client, version, id);
        _resource = resource;

        resource.CreateKeymap += (_, e) =>
        {
            var keymapResource = new RiverXkbKeymapV1Resource(client, version, e.Id);
            var text = ReadKeymap(e.Fd, out var readError);
            if (text is null)
            {
                keymapResource.SendFailure(readError ?? "the keymap fd could not be read");
                return;
            }

            try
            {
                using var context = XkbContext.Create();
                using var compiled = context.CreateKeymapFromString(text);
            }
            catch (XkbException error)
            {
                keymapResource.SendFailure(error.Message);
                return;
            }

            _keymaps[keymapResource] = text;
            keymapResource.DestroyRequest += (_, _) => _keymaps.Remove(keymapResource);
            keymapResource.SendSuccess();
        };

        resource.Stop += (_, _) =>
        {
            resource.SendFinished();
            _resource = null;
        };
        resource.DestroyRequest += (_, _) => _resource = null;
        resource.Destroyed += (_, _) => _resource = null;

        foreach (var keyboard in _keyboards)
        {
            Announce(keyboard);
        }
    }

    private readonly Dictionary<RiverXkbKeymapV1Resource, string> _keymaps = [];

    internal string? KeymapTextOf(RiverXkbKeymapV1Resource? resource) =>
        resource is null ? null : _keymaps.GetValueOrDefault(resource);

    private static string? ReadKeymap(int fd, out string? error)
    {
        error = null;
        if (fd < 0)
        {
            error = "no keymap fd was provided";
            return null;
        }

        try
        {
            if (fstat_size(fd) is not { } size || size <= 0 || size > MaxKeymapBytes)
            {
                error = "the keymap fd is empty or implausibly large";
                return null;
            }

            var data = mmap(nint.Zero, (nuint)size, ProtRead, MapPrivate, fd, 0);
            if (data == FailedMap || data == nint.Zero)
            {
                error = "the keymap fd could not be mapped";
                return null;
            }

            try
            {
                unsafe
                {
                    var span = new ReadOnlySpan<byte>((void*)data, (int)size);
                    var end = span.IndexOf((byte)0);
                    return System.Text.Encoding.UTF8.GetString(end >= 0 ? span[..end] : span);
                }
            }
            finally
            {
                munmap(data, (nuint)size);
            }
        }
        finally
        {
            close(fd);
        }
    }

    private const int MaxKeymapBytes = 8 * 1024 * 1024;
    private const int ProtRead = 1;
    private const int MapPrivate = 2;
    private static readonly nint FailedMap = -1;

    private static long? fstat_size(int fd)
    {
        var size = lseek(fd, 0, SeekEnd);
        _ = lseek(fd, 0, SeekSet);
        return size < 0 ? null : size;
    }

    private const int SeekSet = 0;
    private const int SeekEnd = 2;

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(nint addr, nuint length);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
