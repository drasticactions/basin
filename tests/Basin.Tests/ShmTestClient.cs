using System.Runtime.InteropServices;
using Wayland;

namespace Basin.Tests;

internal sealed class ShmTestClient : IDisposable
{
    private readonly List<ClientShmBuffer> _buffers = [];
    private readonly List<(uint Name, string Interface, uint Version)> _globals = [];

    public ShmTestClient(int fd)
    {
        Display = WlDisplay.ConnectToFd(fd);
    }

    public WlDisplay Display { get; }

    public WlRegistry Registry { get; private set; } = null!;

    public WlCompositor Compositor { get; private set; } = null!;

    public WlSubcompositor Subcompositor { get; private set; } = null!;

    public WlShm Shm { get; private set; } = null!;

    public WlSeat? Seat { get; private set; }

    public WlDataDeviceManager? DataDeviceManager { get; private set; }

    public Basin.Shell.Xdg.Protocol.XdgWmBase? WmBase { get; private set; }

    public Basin.Shell.Xdg.Protocol.ZxdgDecorationManagerV1? DecorationManager { get; private set; }

    public Basin.Protocol.WpViewporter? Viewporter { get; private set; }

    public Basin.Desktop.Protocol.WpFractionalScaleManagerV1? FractionalScale { get; private set; }

    public Basin.Protocol.WpPresentation? Presentation { get; private set; }

    public uint PresentationClockId { get; private set; }

    public Basin.Protocol.ZwpLinuxDmabufV1? Dmabuf { get; private set; }

    public List<WlOutput> Outputs { get; } = [];

    public List<uint> ShmFormats { get; } = [];

    public IReadOnlyList<(uint Name, string Interface, uint Version)> Globals => _globals;

    public T BindAt<T>(string name, uint version)
        where T : WlProxy, IWaylandObject<T> =>
        Bind<T>(_globals, name, version);

    public void BindGlobals(Action pumpToClient)
    {
        Registry = Display.GetRegistry();
        var globals = _globals;
        Registry.Global += (_, e) => globals.Add((e.Name, e.Interface, e.Version));
        pumpToClient();

        Compositor = Bind<WlCompositor>(globals, "wl_compositor", 7);
        Subcompositor = Bind<WlSubcompositor>(globals, "wl_subcompositor", 1);
        Shm = Bind<WlShm>(globals, "wl_shm", 1);
        Shm.FormatEvent += (_, e) => ShmFormats.Add((uint)e.Format);
        if (globals.Exists(g => g.Interface == "wl_seat"))
        {
            Seat = Bind<WlSeat>(globals, "wl_seat", 9);
        }

        if (globals.Exists(g => g.Interface == "wl_data_device_manager"))
        {
            DataDeviceManager = Bind<WlDataDeviceManager>(globals, "wl_data_device_manager", 3);
        }

        if (globals.Exists(g => g.Interface == "xdg_wm_base"))
        {
            WmBase = Bind<Basin.Shell.Xdg.Protocol.XdgWmBase>(globals, "xdg_wm_base", 7);
        }

        if (globals.Exists(g => g.Interface == "zxdg_decoration_manager_v1"))
        {
            DecorationManager = Bind<Basin.Shell.Xdg.Protocol.ZxdgDecorationManagerV1>(globals, "zxdg_decoration_manager_v1", 1);
        }

        if (globals.Exists(g => g.Interface == "wp_viewporter"))
        {
            Viewporter = Bind<Basin.Protocol.WpViewporter>(globals, "wp_viewporter", 1);
        }

        if (globals.Exists(g => g.Interface == "wp_fractional_scale_manager_v1"))
        {
            FractionalScale = Bind<Basin.Desktop.Protocol.WpFractionalScaleManagerV1>(globals, "wp_fractional_scale_manager_v1", 1);
        }

        if (globals.Exists(g => g.Interface == "wp_presentation"))
        {
            Presentation = Bind<Basin.Protocol.WpPresentation>(globals, "wp_presentation", 1);
            Presentation.ClockId += (_, e) => PresentationClockId = e.ClkId;
        }

        if (globals.Exists(g => g.Interface == "zwp_linux_dmabuf_v1"))
        {
            Dmabuf = Bind<Basin.Protocol.ZwpLinuxDmabufV1>(globals, "zwp_linux_dmabuf_v1", 4);
        }

        foreach (var entry in globals)
        {
            if (entry.Interface == "wl_output")
            {
                Outputs.Add(Registry.Bind<WlOutput>(entry.Name, Math.Min(4u, entry.Version)));
            }
        }

        pumpToClient();
    }

    public ClientShmBuffer CreateBuffer(int width, int height, Action<nint, int> fill)
    {
        var buffer = new ClientShmBuffer(Shm, width, height);
        fill(buffer.Data, buffer.Stride);
        _buffers.Add(buffer);
        return buffer;
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        try
        {
            Display.Flush();
        }
        catch (WaylandException)
        {
        }

        Display.Dispose();
    }

    private T Bind<T>(List<(uint Name, string Interface, uint Version)> globals, string name, uint version)
        where T : WlProxy, IWaylandObject<T>
    {
        var entry = globals.Find(g => g.Interface == name);
        if (entry.Name == 0)
        {
            throw new InvalidOperationException($"Server did not advertise {name}.");
        }

        return Registry.Bind<T>(entry.Name, Math.Min(version, entry.Version));
    }
}

internal sealed unsafe class ClientShmBuffer : IDisposable
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;

    private readonly int _fd;
    private readonly int _size;
    private void* _map;

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    public ClientShmBuffer(WlShm shm, int width, int height)
    {
        Width = width;
        Height = height;
        Stride = width * 4;
        _size = Stride * height;

        _fd = memfd_create("basin-test-shm", 1 );
        if (_fd < 0 || ftruncate(_fd, _size) != 0)
        {
            throw new InvalidOperationException("memfd_create/ftruncate failed.");
        }

        _map = mmap(null, (nuint)_size, ProtReadWrite, MapShared, _fd, 0);
        if ((nint)_map == -1)
        {
            throw new InvalidOperationException("mmap failed.");
        }

        var pool = shm.CreatePool(_fd, _size);
        Proxy = pool.CreateBuffer(0, width, height, Stride, WlShm.Format.Xrgb8888);
        pool.Dispose();
    }

    public WlBuffer Proxy { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public nint Data => (nint)_map;

    public bool Released { get; private set; }

    public void TrackRelease()
    {
        Proxy.Release += (_, _) => Released = true;
    }

    public void ResetReleased() => Released = false;

    public void Dispose()
    {
        if (_map != null)
        {
            munmap(_map, (nuint)_size);
            _map = null;
            close(_fd);
            if (!Proxy.IsDestroyed)
            {
                Proxy.Dispose();
            }
        }
    }
}
