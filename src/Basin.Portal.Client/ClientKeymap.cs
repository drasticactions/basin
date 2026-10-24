using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using Xkb;

namespace Basin.Portal.Client;

/// <summary>
/// Holds the XKB keymap that the compositor sent to the seat keyboard of a portal client.
/// It keeps a sealed copy in a memfd so that virtual keyboards can upload the same keymap.
/// </summary>
public sealed unsafe class ClientKeymap : IActiveKeymap, IKeymapLookup, IDisposable
{
    private const int ProtRead = 1;
    private const int MapShared = 1;

    private XkbContext? _context;
    private XkbKeymap? _keymap;
    private XkbState? _state;
    private int _fd = -1;
    private uint _size;

    /// <summary>
    /// Gets the memfd and the size in bytes of the stored keymap text, with its NUL terminator.
    /// The class owns the fd. It is <see langword="null"/> before the first successful compile.
    /// </summary>
    public (int Fd, uint Size)? KeymapBuffer => _fd >= 0 ? (_fd, _size) : null;

    /// <summary>
    /// Occurs after <see cref="Compile"/> replaces the keymap. A keymap that does not compile does not raise it.
    /// </summary>
    public event Action? KeymapChanged;

    /// <summary>
    /// Maps a keymap fd from a <c>wl_keyboard.keymap</c> event read-only and compiles its text. The caller keeps ownership of <paramref name="fd"/>.
    /// The call does nothing when the map fails.
    /// </summary>
    /// <param name="fd">The fd that holds the XKB text keymap.</param>
    /// <param name="size">The size of the mapping in bytes. Text after the first NUL byte is ignored.</param>
    public void Load(int fd, uint size)
    {
        var map = mmap(null, size, ProtRead, MapShared, fd, 0);
        if ((nint)map == -1)
        {
            return;
        }

        string text;
        try
        {
            var span = new ReadOnlySpan<byte>(map, (int)size);
            var end = span.IndexOf((byte)0);
            text = Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
        }
        finally
        {
            munmap(map, size);
        }

        Compile(text);
    }

    /// <summary>
    /// Compiles an XKB text keymap and makes it the active keymap. It then stores a copy in a new memfd and raises <see cref="KeymapChanged"/>.
    /// Text that does not compile leaves the current keymap in place.
    /// </summary>
    /// <param name="text">The keymap in the XKB text v1 format.</param>
    public void Compile(string text)
    {
        _context ??= XkbContext.Create();
        var keymap = _context.CreateKeymapFromBuffer(Encoding.UTF8.GetBytes(text), XkbKeymapFormat.TextV1);
        if (keymap is null)
        {
            return;
        }

        _state?.Dispose();
        _keymap?.Dispose();
        _keymap = keymap;
        _state = keymap.CreateState();
        Store(text);
        KeymapChanged?.Invoke();
    }

    /// <summary>
    /// Finds the first key and shift level in the effective layout that produce a keysym.
    /// </summary>
    /// <param name="keysym">The XKB keysym to find.</param>
    /// <param name="keycode">The evdev keycode of the key, or 0 when no key matches.</param>
    /// <param name="modifiers">The first modifier mask that selects the level, or 0.</param>
    /// <returns><see langword="true"/> when a key matches. It is <see langword="false"/> when no keymap is loaded or no key matches.</returns>
    public bool TryKeycodeForKeysym(uint keysym, out uint keycode, out uint modifiers)
    {
        keycode = 0;
        modifiers = 0;
        if (_keymap is not { } keymap || _state is not { } state)
        {
            return false;
        }

        var layout = state.SerializeLayout(XkbStateComponent.LayoutEffective);
        foreach (var candidate in keymap.GetKeycodes())
        {
            var levels = keymap.GetNumLevelsForKey(candidate, layout);
            for (var level = 0u; level < levels; level++)
            {
                var syms = keymap.GetKeySymsByLevel(candidate, layout, level);
                for (var i = 0; i < syms.Length; i++)
                {
                    if ((uint)syms[i] != keysym)
                    {
                        continue;
                    }

                    keycode = candidate - 8;
                    var masks = keymap.GetModsForLevel(candidate, layout, level);
                    modifiers = masks.Length > 0 ? masks[0] : 0;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the keysym that a key produces in the current keyboard state, with the modifiers and the layout group from <see cref="UpdateModifiers"/>.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <returns>The keysym, or 0 when no keymap is loaded or the key produces no single keysym.</returns>
    public uint KeysymForKeycode(uint keycode) => _state is { } state ? state.GetKeyOneSym(keycode + 8).Value : 0;

    /// <summary>
    /// Gets the keysym that a key produces at level 0 of the effective layout. Active modifiers have no effect on the result.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <returns>The keysym, or 0 when no keymap is loaded or the key has no symbol.</returns>
    public uint RawKeysymForKeycode(uint keycode)
    {
        if (_keymap is not { } keymap || _state is not { } state)
        {
            return 0;
        }

        var layout = state.SerializeLayout(XkbStateComponent.LayoutEffective);
        var syms = keymap.GetKeySymsByLevel(keycode + 8, layout, 0);
        return syms.Length > 0 ? (uint)syms[0] : 0;
    }

    /// <summary>
    /// Applies a <c>wl_keyboard.modifiers</c> event to the keyboard state. It sets the effective layout that the lookups read.
    /// </summary>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout group.</param>
    public void UpdateModifiers(uint depressed, uint latched, uint locked, uint group) =>
        _state?.UpdateMask(depressed, latched, locked, 0, 0, group);

    /// <summary>
    /// Releases the XKB objects and closes the stored keymap fd.
    /// </summary>
    public void Dispose()
    {
        _state?.Dispose();
        _keymap?.Dispose();
        _context?.Dispose();
        _state = null;
        _keymap = null;
        _context = null;
        if (_fd >= 0)
        {
            close(_fd);
            _fd = -1;
        }
    }

    private void Store(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var size = bytes.Length + 1;
        var fd = memfd_create("basin-client-keymap", 1);
        if (fd < 0 || ftruncate(fd, size) != 0)
        {
            if (fd >= 0)
            {
                close(fd);
            }

            return;
        }

        var map = mmap(null, (uint)size, 3, MapShared, fd, 0);
        if ((nint)map != -1)
        {
            bytes.AsSpan().CopyTo(new Span<byte>(map, bytes.Length));
            ((byte*)map)[bytes.Length] = 0;
            munmap(map, (uint)size);
        }

        if (_fd >= 0)
        {
            close(_fd);
        }

        _fd = fd;
        _size = (uint)size;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(void* addr, nuint length);

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc")]
    private static extern int close(int fd);
}
