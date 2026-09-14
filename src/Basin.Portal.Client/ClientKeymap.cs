using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using Xkb;

namespace Basin.Portal.Client;

public sealed unsafe class ClientKeymap : IActiveKeymap, IKeymapLookup, IDisposable
{
    private const int ProtRead = 1;
    private const int MapShared = 1;

    private XkbContext? _context;
    private XkbKeymap? _keymap;
    private XkbState? _state;
    private int _fd = -1;
    private uint _size;

    public (int Fd, uint Size)? KeymapBuffer => _fd >= 0 ? (_fd, _size) : null;

    public event Action? KeymapChanged;

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

    public uint KeysymForKeycode(uint keycode)
    {
        if (_keymap is not { } keymap || _state is not { } state)
        {
            return 0;
        }

        var layout = state.SerializeLayout(XkbStateComponent.LayoutEffective);
        var syms = keymap.GetKeySymsByLevel(keycode + 8, layout, 0);
        return syms.Length > 0 ? (uint)syms[0] : 0;
    }

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
