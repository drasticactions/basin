using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Basin.Cli;
using BasinChurn.Protocol;
using Microsoft.Extensions.Logging;
using Wayland;

namespace BasinChurn;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "Opens, resizes, retitles and closes windows on a schedule, so a session can be soaked.");
        var socketOption = cli.Add(CommonOptions.Socket());
        var seedOption = cli.Add(new Option<int>("--seed")
        {
            Description = "the seed every choice comes from, so a run repeats",
            HelpName = "N",
            DefaultValueFactory = _ => 1,
        });
        var windowsOption = cli.Add(new Option<int>("--windows")
        {
            Description = "how many windows to keep open at most",
            HelpName = "N",
            DefaultValueFactory = _ => 4,
        });
        var rateOption = cli.Add(new Option<int>("--rate")
        {
            Description = "actions a second",
            HelpName = "N",
            DefaultValueFactory = _ => 20,
        });
        var secondsOption = cli.Add(new Option<int>("--seconds")
        {
            Description = "stop after this long, or 0 to run until stopped",
            HelpName = "N",
            DefaultValueFactory = _ => 0,
        });

        return cli.Run(args, result =>
        {
            using var loggers = cli.CreateLoggerFactory(result);
            return Run(
                loggers.CreateLogger("BasinChurn"),
                result.GetValue(socketOption),
                result.GetValue(seedOption),
                Math.Max(1, result.GetValue(windowsOption)),
                Math.Max(1, result.GetValue(rateOption)),
                result.GetValue(secondsOption));
        });
    }

    private static int Run(ILogger log, string? socket, int seed, int maxWindows, int rate, int seconds)
    {
        using var display = socket is null ? WlDisplay.Connect() : WlDisplay.Connect(socket);
        var registry = display.GetRegistry();

        WlCompositor? compositor = null;
        WlShm? shm = null;
        WlSubcompositor? subcompositor = null;
        XdgWmBase? wmBase = null;

        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "wl_compositor":
                    compositor = registry.Bind<WlCompositor>(e.Name, Math.Min(4u, e.Version));
                    break;
                case "wl_shm":
                    shm = registry.Bind<WlShm>(e.Name, 1);
                    break;
                case "wl_subcompositor":
                    subcompositor = registry.Bind<WlSubcompositor>(e.Name, 1);
                    break;
                case "xdg_wm_base":
                    wmBase = registry.Bind<XdgWmBase>(e.Name, 1);
                    break;
            }
        };
        display.Roundtrip();

        if (compositor is null || shm is null || wmBase is null)
        {
            log.LogError("compositor is missing wl_compositor, wl_shm or xdg_wm_base");
            return 1;
        }

        wmBase.Ping += (_, e) => wmBase.Pong(e.Serial);

        var running = true;
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            running = false;
            context.Cancel = true;
        });
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            running = false;
            context.Cancel = true;
        });

        var random = new Random(seed);
        var windows = new List<ChurnWindow>();
        var deadline = seconds > 0 ? Stopwatch.GetTimestamp() + (seconds * Stopwatch.Frequency) : long.MaxValue;
        var delay = 1000 / rate;
        var actions = 0L;

        Console.WriteLine($"CHURN seed={seed} windows={maxWindows} rate={rate}");

        while (running && Stopwatch.GetTimestamp() < deadline)
        {
            var choice = windows.Count == 0 ? 0 : random.Next(6);
            switch (choice)
            {
                case 0 when windows.Count < maxWindows:
                    windows.Add(ChurnWindow.Map(compositor, shm, wmBase, subcompositor, random, windows.Count));
                    break;
                case 1 when windows.Count > 0:
                    var index = random.Next(windows.Count);
                    windows[index].Dispose();
                    windows.RemoveAt(index);
                    break;
                case 2:
                    Pick(windows, random).Resize(shm, 60 + random.Next(240), 40 + random.Next(200));
                    break;
                case 3:
                    Pick(windows, random).Retitle(random.Next());
                    break;
                case 4:
                    Pick(windows, random).Redraw();
                    break;
                case 5:
                    Pick(windows, random).ToggleChild(compositor, shm, subcompositor);
                    break;
                default:
                    break;
            }

            actions++;
            display.Flush();
            display.Dispatch(delay);
        }

        foreach (var window in windows)
        {
            window.Dispose();
        }

        display.Roundtrip();
        Console.WriteLine($"CHURN done actions={actions}");
        return 0;
    }

    private static ChurnWindow Pick(List<ChurnWindow> windows, Random random) => windows[random.Next(windows.Count)];
}

internal sealed class ChurnWindow : IDisposable
{
    private readonly WlSurface _surface;
    private readonly XdgSurface _xdgSurface;
    private readonly XdgToplevel _toplevel;
    private ShmBuffer _buffer;
    private WlSurface? _child;
    private WlSubsurface? _subsurface;
    private ShmBuffer? _childBuffer;
    private uint _color;

    private ChurnWindow(WlSurface surface, XdgSurface xdgSurface, XdgToplevel toplevel, ShmBuffer buffer, uint color)
    {
        _surface = surface;
        _xdgSurface = xdgSurface;
        _toplevel = toplevel;
        _buffer = buffer;
        _color = color;
    }

    public static ChurnWindow Map(
        WlCompositor compositor,
        WlShm shm,
        XdgWmBase wmBase,
        WlSubcompositor? subcompositor,
        Random random,
        int ordinal)
    {
        _ = subcompositor;
        var surface = compositor.CreateSurface();
        var xdgSurface = wmBase.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetTitle($"churn {ordinal}");
        toplevel.SetAppId("basin.churn");

        var width = 80 + random.Next(160);
        var height = 60 + random.Next(120);
        var buffer = new ShmBuffer(shm, width, height);
        var color = 0xFF000000u | (uint)random.Next(0xFFFFFF);

        var window = new ChurnWindow(surface, xdgSurface, toplevel, buffer, color);
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            window.Redraw();
        };

        surface.Commit();
        return window;
    }

    public void Resize(WlShm shm, int width, int height)
    {
        _buffer.Dispose();
        _buffer = new ShmBuffer(shm, width, height);
        Redraw();
    }

    public void Retitle(int token)
    {
        _toplevel.SetTitle($"churn {token:X8}");
        _toplevel.SetAppId(token % 2 == 0 ? "basin.churn" : "basin.churn.alt");
        _surface.Commit();
    }

    public void Redraw()
    {
        _color = (_color & 0xFF000000u) | ((_color + 0x00112233u) & 0x00FFFFFFu);
        Fill(_buffer, _color);
        _surface.Attach(_buffer.Proxy, 0, 0);
        _surface.Damage(0, 0, _buffer.Width, _buffer.Height);
        _surface.Commit();
    }

    public void ToggleChild(WlCompositor compositor, WlShm shm, WlSubcompositor? subcompositor)
    {
        if (subcompositor is null)
        {
            return;
        }

        if (_child is not null)
        {
            _subsurface?.Dispose();
            _childBuffer?.Dispose();
            _child.Destroy();
            _child = null;
            _subsurface = null;
            _childBuffer = null;
            _surface.Commit();
            return;
        }

        _child = compositor.CreateSurface();
        _subsurface = subcompositor.GetSubsurface(_child, _surface);
        _subsurface.SetPosition(8, 8);
        _childBuffer = new ShmBuffer(shm, 24, 24);
        Fill(_childBuffer, 0xFFEE4444);
        _child.Attach(_childBuffer.Proxy, 0, 0);
        _child.Damage(0, 0, 24, 24);
        _child.Commit();
        _surface.Commit();
    }

    public void Dispose()
    {
        _subsurface?.Dispose();
        _childBuffer?.Dispose();
        _child?.Destroy();
        _toplevel.Dispose();
        _xdgSurface.Dispose();
        _surface.Destroy();
        _buffer.Dispose();
    }

    private static unsafe void Fill(ShmBuffer buffer, uint color)
    {
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(buffer.Data + (y * buffer.Stride));
            for (var x = 0; x < buffer.Width; x++)
            {
                row[x] = color;
            }
        }
    }
}

internal sealed unsafe class ShmBuffer : IDisposable
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

    public ShmBuffer(WlShm shm, int width, int height)
    {
        Width = width;
        Height = height;
        Stride = width * 4;
        _size = Stride * height;

        _fd = memfd_create("basin-churn-shm", 1);
        if (_fd < 0 || ftruncate(_fd, _size) != 0)
        {
            throw new InvalidOperationException("memfd_create/ftruncate failed");
        }

        _map = mmap(null, (nuint)_size, ProtReadWrite, MapShared, _fd, 0);
        if ((nint)_map == -1)
        {
            throw new InvalidOperationException("mmap failed");
        }

        var pool = shm.CreatePool(_fd, _size);
        Proxy = pool.CreateBuffer(0, width, height, Stride, WlShm.Format.Argb8888);
        pool.Dispose();
    }

    public WlBuffer Proxy { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public nint Data => (nint)_map;

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
