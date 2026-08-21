using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Basin;
using Basin.Backend.Wayland;
using Basin.Protocol;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Tests;

internal readonly record struct NestedParentOptions
{
    public bool Decorations { get; init; }

    public bool ServerSideDecorations { get; init; }

    public bool Subcompositor { get; init; }

    public bool Viewporter { get; init; }

    public bool Dmabuf { get; init; }

    public bool PointerGestures { get; init; }

    public bool DataDevice { get; init; }

    public bool Keyboard { get; init; }

    public DrmFormatSet? DmabufFormats { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public static NestedParentOptions Undecorating => new()
    {
        Decorations = false,
        Subcompositor = true,
        Viewporter = true,
        Width = 800,
        Height = 600,
    };

    public static NestedParentOptions Decorating => Undecorating with
    {
        Decorations = true,
        ServerSideDecorations = true,
    };

    public static NestedParentOptions Selecting => Undecorating with
    {
        DataDevice = true,
        Keyboard = true,
    };
}

internal sealed class NestedParent : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly NestedParentOptions _options;
    private const int FGetFl = 3;
    private const int FSetFl = 4;
    private const int FSetFd = 2;
    private const int FdCloexec = 1;
    private const int ONonBlockDarwin = 0x0004;

    private volatile bool _stopping;
    private Exception? _startupError;
    private int _wakeRead = -1;
    private int _wakeWrite = -1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, void* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, void* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    public NestedParent(NestedParentOptions options)
    {
        CompositorTestHost.SkipWithoutWaylandClient();
        _options = options;
        CreateWakePipe();
        _thread = new Thread(Run) { IsBackground = true, Name = "nested-parent" };
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("the parent compositor never came up");
        }

        if (_startupError is not null)
        {
            throw new InvalidOperationException("the parent compositor failed to start", _startupError);
        }
    }

    public string Socket { get; private set; } = string.Empty;

    public List<XdgToplevelWindow> Toplevels { get; } = [];

    public Basin.Seat.Seat? Seat { get; private set; }

    public Basin.Desktop.PointerGesturesManager? Gestures { get; private set; }

    public Basin.Seat.DataDeviceManager? DataDevices { get; private set; }

    public T Invoke<T>(Func<T> body)
    {
        var result = default(T)!;
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        _work.Enqueue(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                done.Set();
            }
        });

        Wake();
        if (!done.Wait(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("the parent compositor stopped answering");
        }

        return failure is not null
            ? throw new InvalidOperationException("the parent compositor threw", failure)
            : result;
    }

    public void Invoke(Action body) => Invoke<object?>(() =>
    {
        body();
        return null;
    });

    public void Dispose()
    {
        _stopping = true;
        Wake();
        _thread.Join(TimeSpan.FromSeconds(20));
        CloseWakePipe();
        _started.Dispose();
    }

    private unsafe void CreateWakePipe()
    {
        var fds = stackalloc int[2];
        if (OperatingSystem.IsLinux())
        {
            if (pipe2(fds, 0x80000 | 0x800) != 0)
            {
                throw new InvalidOperationException("wake pipe creation failed");
            }
        }
        else
        {
            if (pipe(fds) != 0)
            {
                throw new InvalidOperationException("wake pipe creation failed");
            }

            for (var i = 0; i < 2; i++)
            {
                _ = fcntl(fds[i], FSetFd, FdCloexec);
                _ = fcntl(fds[i], FSetFl, fcntl(fds[i], FGetFl, 0) | ONonBlockDarwin);
            }
        }

        _wakeRead = fds[0];
        _wakeWrite = fds[1];
    }

    private void CloseWakePipe()
    {
        if (_wakeRead >= 0)
        {
            close(_wakeRead);
            _wakeRead = -1;
        }

        if (_wakeWrite >= 0)
        {
            close(_wakeWrite);
            _wakeWrite = -1;
        }
    }

    private unsafe void Wake()
    {
        if (_wakeWrite < 0)
        {
            return;
        }

        byte one = 1;
        _ = write(_wakeWrite, &one, 1);
    }

    private unsafe void Run()
    {
        WlServerDisplay? display = null;
        WaylandEventLoop? loop = null;
        ClientBufferRegistry? buffers = null;
        CompositorGlobal? compositor = null;
        SubcompositorGlobal? subcompositor = null;
        ViewporterGlobal? viewporter = null;
        Basin.Seat.Seat? seat = null;
        LinuxDmabufGlobal? dmabuf = null;
        XdgShell? shell = null;
        XdgDecorationManager? decorations = null;
        Basin.Desktop.PointerGesturesManager? gestures = null;
        Basin.Seat.DataDeviceManager? dataDevices = null;
        IEventSource? wake = null;
        try
        {
            display = WlServerDisplay.Create();
            loop = new WaylandEventLoop(display);
            buffers = new ClientBufferRegistry();
            _ = new ShmGlobal(display, buffers: buffers);
            compositor = new CompositorGlobal(display, buffers);
            if (_options.Subcompositor)
            {
                subcompositor = new SubcompositorGlobal(display, compositor);
            }

            if (_options.Viewporter)
            {
                viewporter = new ViewporterGlobal(display, compositor);
            }

            if (_options.Dmabuf && _options.DmabufFormats is { } dmabufFormats)
            {
                dmabuf = new LinuxDmabufGlobal(
                    display, buffers, dmabufFormats, CompositorTestHost.RenderNodePath, compositor: compositor);
            }

            var capabilities = Basin.Seat.SeatCapability.Pointer;
            if (_options.Keyboard)
            {
                capabilities |= Basin.Seat.SeatCapability.Keyboard;
            }

            seat = new Basin.Seat.Seat(display, compositor, capabilities: capabilities);
            if (_options.DataDevice)
            {
                seat.DataDevice.Store = new Basin.Seat.SeatSelectionStore(seat);
                dataDevices = new Basin.Seat.DataDeviceManager(display, seat);
                DataDevices = dataDevices;
            }

            if (_options.PointerGestures)
            {
                gestures = new Basin.Desktop.PointerGesturesManager(display, seat);
            }

            Seat = seat;
            Gestures = gestures;

            shell = new XdgShell(display, compositor, seat);
            if (_options.Decorations)
            {
                decorations = new XdgDecorationManager(display)
                {
                    DefaultMode = _options.ServerSideDecorations
                        ? DecorationMode.ServerSide
                        : DecorationMode.ClientSide,
                };
            }

            shell.NewToplevel += toplevel =>
            {
                Toplevels.Add(toplevel);

                toplevel.SetSize(_options.Width, _options.Height);
                toplevel.SetActivated(true);
            };

            wake = loop.AddFd(_wakeRead, FdReadiness.Readable, (fd, _) =>
            {
                var scratch = stackalloc byte[64];
                while (read(fd, scratch, 64) > 0)
                {
                }

                while (_work.TryDequeue(out var item))
                {
                    item();
                }
            });

            Socket = display.AddSocketAuto();
            _started.Set();
            while (!_stopping)
            {
                loop.Dispatch(-1);
            }
        }
        catch (Exception e)
        {
            _startupError ??= e;
            _started.Set();
        }
        finally
        {
            wake?.Remove();
            dataDevices?.Dispose();
            gestures?.Dispose();
            decorations?.Dispose();
            shell?.Dispose();
            dmabuf?.Dispose();
            seat?.Dispose();
            viewporter?.Dispose();
            subcompositor?.Dispose();
            compositor?.Dispose();
            display?.Dispose();
        }
    }
}

internal sealed class NestedBackendTestHost : IDisposable
{
    private readonly WlServerDisplay _display;
    private bool _disposed;

    public NestedBackendTestHost(NestedParentOptions options)
    {
        Parent = new NestedParent(options);

        _display = WlServerDisplay.Create();
        Loop = new WaylandEventLoop(_display);
        Backend = new WaylandBackend(Loop, Parent.Socket);
        Backend.Start();
    }

    public NestedParent Parent { get; }

    public WaylandEventLoop Loop { get; }

    public WaylandBackend Backend { get; }

    public WaylandOutput CreateOutput()
    {
        var output = Backend.CreateOutput();
        Pump();
        return output;
    }

    public void Pump(int rounds = 4)
    {
        for (var i = 0; i < rounds; i++)
        {
            Backend.Flush();
            Backend.Roundtrip();
            Loop.Dispatch(0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Backend.Dispose();
        _display.Dispose();
        Parent.Dispose();
    }
}
