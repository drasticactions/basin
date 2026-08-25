using System.Runtime.InteropServices;
using Basin.Backend.Headless;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Renderers;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Tests;

internal sealed class CompositorTestHost : IDisposable
{
    private const int AF_UNIX = 1;
    private const int SOCK_STREAM = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* sv);

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int poll(PollFd* fds, nuint nfds, int timeout);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    private readonly Dictionary<string, int> _fdBaseline;
    private readonly Dictionary<string, int> _fdAllowance;
    private uint _frameTimestamp;

    /// <summary>
    /// Whether this host has a libwayland client to drive a compositor with.
    /// The managed transport replaces the server half only, so a test that needs
    /// a client needs this.
    /// </summary>
    public static bool HasWaylandClient { get; } =
        System.Runtime.InteropServices.NativeLibrary.TryLoad("wayland-client", out _) ||
        System.Runtime.InteropServices.NativeLibrary.TryLoad("libwayland-client.so.0", out _);

    public static void SkipWithoutWaylandClient() =>
        Xunit.Assert.SkipWhen(
            !HasWaylandClient,
            "this host has no libwayland client, and the suite drives the compositor with one");

    /// <summary>
    /// Whether this host has a libwayland server to create a display with. The
    /// managed transport replaces that half, so only a libwayland run needs it.
    /// </summary>
    public static bool HasWaylandServer { get; } =
        System.Runtime.InteropServices.NativeLibrary.TryLoad("wayland-server", out _) ||
        System.Runtime.InteropServices.NativeLibrary.TryLoad("libwayland-server.so.0", out _);

    public static void SkipWithoutWaylandServer() =>
        Xunit.Assert.SkipWhen(
            TransportUnderTest == TransportKind.LibWayland && !HasWaylandServer,
            "this host has no libwayland server, and this run is on the libwayland transport");

    public static TransportKind TransportUnderTest { get; } =
        Environment.GetEnvironmentVariable("BASIN_TEST_TRANSPORT") switch
        {
            "managed" => TransportKind.Managed,
            "libwayland" or null or "" => TransportKind.LibWayland,
            var other => throw new InvalidOperationException(
                $"BASIN_TEST_TRANSPORT names no transport: '{other}' (expected libwayland or managed)"),
        };

    public CompositorTestHost(int width = 160, int height = 120, string renderer = "pixman")
    {
        SkipWithoutWaylandClient();
        BasinCounters.Reset();

        _fdAllowance = DriverFdResidue.For(renderer);
        TestLogging.WarmStreams();
        _fdBaseline = FdSnapshot.Take();
        var stack = RendererCatalog.Create(renderer, RenderNodePath);
        Renderer = stack.Renderer;

        stack.DeviceAllocator?.Dispose();

        Display = TransportUnderTest == TransportKind.Managed
            ? WlServerDisplay.Create(new ManagedTransport())
            : WlServerDisplay.Create();
        Loop = new WaylandEventLoop(Display);
        Buffers = new ClientBufferRegistry();
        _ = new ShmGlobal(Display, buffers: Buffers);
        Compositor = new CompositorGlobal(Display, Buffers);
        Subcompositor = new SubcompositorGlobal(Display, Compositor);
        Seat = new Basin.Seat.Seat(Display, Compositor, capabilities:
            Basin.Seat.SeatCapability.Pointer | Basin.Seat.SeatCapability.Keyboard | Basin.Seat.SeatCapability.Touch);
        DataDevices = new Basin.Seat.DataDeviceManager(Display, Seat);
        Shell = new Basin.Shell.Xdg.XdgShell(Display, Compositor, Seat);
        Decorations = new Basin.Shell.Xdg.XdgDecorationManager(Display);
        Viewporter = new ViewporterGlobal(Display, Compositor);
        Presentation = new PresentationTimeGlobal(Display, Compositor);
        if (File.Exists(RenderNodePath))
        {
            var formats = Renderer.DmabufTextureFormats;
            if (formats.Count == 0)
            {
                formats = new DrmFormatSet();
                formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
                formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierInvalid);
                formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
                formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierInvalid);
            }

            Dmabuf = new LinuxDmabufGlobal(Display, Buffers, formats, RenderNodePath, compositor: Compositor);
        }

        Backend = new HeadlessBackend(Loop);
        Output = Backend.CreateOutput(new OutputMode(width, height, 60_000), manualFrameClock: true);
        OutputGlobal = new OutputGlobal(Display, Output);
        Layout = new OutputLayout();
        Layout.Add(Output, 0, 0);
        FractionalScale = new Basin.Desktop.FractionalScaleManager(Display, Compositor, Layout);
        _outputGlobals.Add((Output, OutputGlobal));
        Scene = new Scene.Scene();
        Target = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);

        Compositor.SurfaceCreated += surface =>
        {
            var sceneSurface = new SceneSurface(Scene.Root, surface);
            SurfaceScenes.Add(sceneSurface);
            sceneSurface.Destroyed += () => SurfaceScenes.Remove(sceneSurface);
            surface.Committed += () =>
            {
                if (surface.SubsurfaceRole is not null && !sceneSurface.IsDestroyed)
                {
                    sceneSurface.Destroy();
                }
            };
        };

        Client = ConnectClient();
    }

    public void DisconnectClient(ShmTestClient client)
    {
        _clients.Remove(client);
        client.Dispose();
    }

    public ShmTestClient AdoptClient(int clientFd)
    {
        var client = new ShmTestClient(clientFd);
        _clients.Add(client);
        client.BindGlobals(() => PumpToClient(client));
        return client;
    }

    public ShmTestClient ConnectClient()
    {
        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (socketpair(AF_UNIX, SOCK_STREAM, 0, fds) != 0)
            {
                throw new InvalidOperationException("socketpair failed.");
            }

            serverFd = fds[0];
            clientFd = fds[1];
        }

        Display.CreateClient(serverFd);
        var client = new ShmTestClient(clientFd);
        _clients.Add(client);
        client.BindGlobals(() => PumpToClient(client));
        return client;
    }

    public WlServerDisplay Display { get; }

    public WaylandEventLoop Loop { get; }

    public ClientBufferRegistry Buffers { get; }

    public CompositorGlobal Compositor { get; }

    public SubcompositorGlobal Subcompositor { get; }

    public Basin.Seat.Seat Seat { get; }

    public Basin.Seat.DataDeviceManager DataDevices { get; }

    public Basin.Shell.Xdg.XdgShell Shell { get; }

    public Basin.Shell.Xdg.XdgDecorationManager Decorations { get; }

    public ViewporterGlobal Viewporter { get; }

    public Basin.Desktop.FractionalScaleManager FractionalScale { get; }

    public PresentationTimeGlobal Presentation { get; }

    public LinuxDmabufGlobal? Dmabuf { get; }

    public static bool RendererNeedsGpu(string renderer) => RendererCatalog.NeedsGpu(renderer);

    public static bool RendererNeedsVulkan(string renderer) => renderer is "vulkan" or "skia-vulkan" or "skia-graphite";

    public static void SkipUnlessRunnable(string renderer)
    {
        Xunit.Assert.SkipWhen(!RendererCatalog.Names.Contains(renderer), $"{renderer} is not in this build");
        Xunit.Assert.SkipWhen(RendererNeedsGpu(renderer) && !File.Exists(RenderNodePath), "no render node");
        Xunit.Assert.SkipWhen(RendererNeedsVulkan(renderer) && !VulkanRunnability.Runnable, VulkanRunnability.Blocker);
    }

    public static void SkipUnlessVulkanRunnable()
    {
        Xunit.Assert.SkipWhen(!File.Exists(RenderNodePath), "no render node");
        Xunit.Assert.SkipWhen(!VulkanRunnability.Runnable, VulkanRunnability.Blocker);
    }

    public static bool IsRunnable(string renderer) =>
        RendererCatalog.Names.Contains(renderer)
        && (!RendererNeedsGpu(renderer) || File.Exists(RenderNodePath))
        && (!RendererNeedsVulkan(renderer) || VulkanRunnability.Runnable);

    public static string RenderNodePath { get; } = PickRenderNode();

    private static string PickRenderNode()
    {
        if (Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") is { Length: > 0 } forced)
        {
            return forced;
        }

        var nodes = Directory.Exists("/dev/dri")
            ? Directory.GetFiles("/dev/dri", "renderD*").OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : [];
        var node = nodes.FirstOrDefault(candidate => DrmDriverOf(candidate) != "nvidia")
            ?? nodes.FirstOrDefault()
            ?? "/dev/dri/renderD128";

        if (DrmDriverOf(node) is not (null or "nvidia"))
        {
            const string icdDir = "/usr/share/vulkan/icd.d";
            if (Directory.Exists(icdDir))
            {
                var icds = string.Join(':', Directory.GetFiles(icdDir, "*.json")
                    .Where(p => !Path.GetFileName(p).Contains("nvidia", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.Ordinal));

                setenv("VK_DRIVER_FILES", icds, 1);
                setenv("VK_ICD_FILENAMES", icds, 1);
            }
        }

        return node;
    }

    private static bool NvidiaRenderNode => DrmDriverOf(RenderNodePath) == "nvidia";

    private static bool DriverClosesFdsLazily => NvidiaRenderNode;

    /// <summary>
    /// Whether this renderer's goldens were recorded on hardware that samples the same way this box does.
    /// </summary>
    public static bool GoldensComparable(string renderer) =>
        !(NvidiaRenderNode && renderer == "skia-graphite");

    private static string? DrmDriverOf(string node)
    {
        var driver = $"/sys/class/drm/{Path.GetFileName(node)}/device/driver";
        return Directory.Exists(driver)
            ? Directory.ResolveLinkTarget(driver, returnFinalTarget: true)?.Name
            : null;
    }

    public HeadlessBackend Backend { get; }

    public HeadlessOutput Output { get; }

    public OutputGlobal OutputGlobal { get; }

    public OutputLayout Layout { get; }

    private readonly List<(IOutput Output, OutputGlobal Global)> _outputGlobals = [];

    public void TrackOutputGlobal(IOutput output, OutputGlobal global) => _outputGlobals.Add((output, global));

    public IReadOnlyList<(IOutput Output, OutputGlobal Global)> OutputGlobals() => _outputGlobals;

    public IRenderer Renderer { get; }

    public Scene.Scene Scene { get; }

    public MemoryBuffer Target { get; }

    public OutputState FrameState { get; } = new();

    public ShmTestClient Client { get; }

    public List<SceneSurface> SurfaceScenes { get; } = [];

    private readonly List<ShmTestClient> _clients = [];

    public void PumpToServer()
    {
        foreach (var client in _clients)
        {
            client.Display.Flush();
        }

        Loop.Dispatch(0);
    }

    public void PumpToClient() => PumpToClient(null);

    private void PumpToClient(ShmTestClient? only)
    {
        PumpToServer();
        Display.FlushClients();

        foreach (var client in _clients)
        {
            if (only is not null && client != only)
            {
                continue;
            }

            while (SocketReadable(client))
            {
                client.Display.Dispatch();
            }

            client.Display.DispatchPending();
        }
    }

    public void PumpUntil(Func<bool> condition, int rounds = 20)
    {
        for (var i = 0; i < rounds && !condition(); i++)
        {
            PumpToClient();
        }

        if (!condition())
        {
            throw new TimeoutException("condition not reached while pumping");
        }
    }

    private static bool SocketReadable(ShmTestClient client)
    {
        unsafe
        {
            var pollFd = new PollFd { Fd = client.Display.Fd, Events = 1 };
            return poll(&pollFd, 1, 0) > 0 && (pollFd.REvents & 1) != 0;
        }
    }

    public void RenderFrame()
    {
        Scene.Render(Renderer, Target, RenderColor.Black, Output.Scale);
        FrameState.Clear();
        if (!Output.Commit(FrameState.SetBuffer(Target)))
        {
            throw new InvalidOperationException("Output commit failed.");
        }

        _frameTimestamp += 16;
        foreach (var sceneSurface in SurfaceScenes)
        {
            sceneSurface.SendFrameDone(_frameTimestamp);
        }
    }

    public uint Pixel(int x, int y)
    {
        if (!Target.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            throw new InvalidOperationException("Target has no data.");
        }

        try
        {
            unsafe
            {
                var row = (uint*)(view.Data + y * view.Stride);
                return row[x] | 0xFF000000u;
            }
        }
        finally
        {
            Target.EndDataAccess();
        }
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        Loop.Dispatch(0);
        Loop.Dispatch(0);

        Scene.Root.Destroy();
        Renderer.Dispose();
        Target.Destroy();
        OutputGlobal.Dispose();
        Backend.Dispose();
        Dmabuf?.Dispose();
        Presentation.Dispose();
        FractionalScale.Dispose();
        Viewporter.Dispose();
        Decorations.Dispose();
        Shell.Dispose();
        DataDevices.Dispose();
        Seat.Dispose();
        Subcompositor.Dispose();
        Compositor.Dispose();
        FrameState.Dispose();
        Display.Dispose();

        if (BasinCounters.Enabled)
        {
            if (BasinCounters.LiveObjects != 0)
            {
                FailTeardown(
                    $"{BasinCounters.LiveObjects} objects still live at teardown.{Environment.NewLine}{BasinCounters.CensusReport()}");
            }

            if (BasinCounters.PendingFrees != 0)
            {
                FailTeardown($"{BasinCounters.PendingFrees} deferred frees still pending at teardown.");
            }
        }

        if (Buffers.Count != 0)
        {
            FailTeardown($"{Buffers.Count} client buffers still registered at teardown.");
        }

        var leaked = FdSnapshot.Diff(_fdBaseline, FdSnapshot.Take(), _fdAllowance);
        if (leaked.Count > 0 && !DriverClosesFdsLazily)
        {
            FailTeardown("fds leaked: " + string.Join(", ", leaked.Select(l => $"{l.Count} x {l.Target}")));
        }
    }

    private static void FailTeardown(string message)
    {
        if (Marshal.GetExceptionPointers() != IntPtr.Zero)
        {
            BasinLog.Error($"teardown not clean while the test itself was already failing: {message}");
            return;
        }

        throw new InvalidOperationException(message);
    }
}

internal static class FdSnapshot
{
    private static readonly bool IgnorePipes =
        Environment.GetEnvironmentVariable("BASIN_VULKAN_VALIDATION") is not null;

    public static Dictionary<string, int> Take()
    {
        if (OperatingSystem.IsMacOS())
        {
            return TakeFromLibproc();
        }

        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        var snapshot = new Dictionary<string, int>();
        foreach (var fd in Directory.GetFiles("/proc/self/fd"))
        {
            string? target;
            try
            {
                target = new FileInfo(fd).LinkTarget;
            }
            catch (IOException)
            {
                continue;
            }

            if (target is null || target.EndsWith(".dll", StringComparison.Ordinal))
            {
                continue;
            }

            if (IgnorePipes && target.StartsWith("pipe:", StringComparison.Ordinal))
            {
                continue;
            }

            snapshot[target] = snapshot.GetValueOrDefault(target) + 1;
        }

        return snapshot;
    }

    private const int ProcPidListFds = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcFdInfo
    {
        public int Fd;
        public uint Type;
    }

    [DllImport("libproc", SetLastError = true)]
    private static extern unsafe int proc_pidinfo(int pid, int flavor, ulong arg, void* buffer, int size);

    /// <summary>
    /// The same census where there is no procfs. Darwin reports an fd's kind
    /// rather than its target, so a leak is named by what leaked and not by
    /// which one.
    /// </summary>
    private static unsafe Dictionary<string, int> TakeFromLibproc()
    {
        var snapshot = new Dictionary<string, int>();
        var pid = Environment.ProcessId;
        var bytes = proc_pidinfo(pid, ProcPidListFds, 0, null, 0);
        if (bytes <= 0)
        {
            return snapshot;
        }

        var entries = new ProcFdInfo[(bytes / sizeof(ProcFdInfo)) + 32];
        fixed (ProcFdInfo* buffer = entries)
        {
            bytes = proc_pidinfo(pid, ProcPidListFds, 0, buffer, entries.Length * sizeof(ProcFdInfo));
        }

        if (bytes <= 0)
        {
            return snapshot;
        }

        for (var i = 0; i < bytes / sizeof(ProcFdInfo); i++)
        {
            var kind = entries[i].Type switch
            {
                1 => "vnode:",
                2 => "socket:",
                5 => "kqueue:",
                6 => "pipe:",
                7 => "shm:",
                _ => $"fdtype-{entries[i].Type}:",
            };

            if (IgnorePipes && kind == "pipe:")
            {
                continue;
            }

            snapshot[kind] = snapshot.GetValueOrDefault(kind) + 1;
        }

        return snapshot;
    }

    public static List<(string Target, int Count)> Diff(
        Dictionary<string, int> before,
        Dictionary<string, int> after,
        Dictionary<string, int>? allowance = null)
    {
        var leaked = new List<(string, int)>();
        foreach (var (target, count) in after)
        {
            var delta = count - before.GetValueOrDefault(target) - (allowance?.GetValueOrDefault(target) ?? 0);
            if (delta > 0)
            {
                leaked.Add((target, delta));
            }
        }

        return leaked;
    }
}

internal static class DriverFdResidue
{
    private static readonly Dictionary<string, Dictionary<string, int>> Measured = [];

    public static Dictionary<string, int> For(string renderer)
    {
        if (!CompositorTestHost.RendererNeedsGpu(renderer) || !File.Exists(CompositorTestHost.RenderNodePath))
        {
            return [];
        }

        if (Measured.TryGetValue(renderer, out var residue))
        {
            return residue;
        }

        Cycle(renderer);
        var before = FdSnapshot.Take();
        Cycle(renderer);
        residue = FdSnapshot.Diff(before, FdSnapshot.Take()).ToDictionary(l => l.Target, l => l.Count);
        Measured[renderer] = residue;
        return residue;
    }

    private static void Cycle(string renderer)
    {
        var stack = RendererCatalog.Create(renderer, CompositorTestHost.RenderNodePath);
        stack.DeviceAllocator?.Dispose();
        stack.Renderer.Dispose();
    }
}
