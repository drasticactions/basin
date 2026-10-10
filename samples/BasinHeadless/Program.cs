using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Wayland;
using Wayland.Server;

namespace Basin.Samples.Headless;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("Headless Basin test program.");
        var frames = cli.Add(CommonOptions.Frames());
        var client = cli.Add(CommonOptions.Client());
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var transport = cli.Add(CommonOptions.Transport());
        var renderer = cli.Add(CommonOptions.Renderer(Renderers, DefaultRenderer));
        var channel = cli.Add(CommonOptions.WaypipeListen());
        var gpu = cli.Add(CommonOptions.Gpu());
        var video = cli.Add(CommonOptions.Video());
        var compress = cli.Add(CommonOptions.Compress());
        _ = IpcCli.AddOption(cli);

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var log = BasinLog.For("BasinHeadless");
            if (result.GetValue(client) is not null && result.GetValue(channel) is not null)
            {
                log.Error($"--client spawns a local client and --waypipe-listen waits for a remote one");
                return 1;
            }

            if (result.GetValue(channel) is not null &&
                result.GetValue(transport).Kind == TransportKind.LibWayland &&
                result.GetResult(transport) is { Implicit: false })
            {
                log.Error($"--waypipe-listen replays its channel over the managed transport");
                return 1;
            }

            var status = Run(
                log,
                result.GetValue(frames),
                result.GetValue(client),
                result.GetValue(screenshot),
                result.GetValue(transport),
                result.GetValue(renderer)!,
                result.GetValue(channel),
                result.GetValue(gpu),
                result.GetValue(video)!,
                result.GetValue(compress)!,
                IpcCli.Read(cli, result),
                out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }

    private static readonly string[] Renderers = ["pixman", "skia"];

    private static string DefaultRenderer => OperatingSystem.IsLinux() ? "pixman" : "skia";

    private static IRenderer CreateRenderer(string name) => name switch
    {
        "skia" => new Basin.Render.Skia.SkiaRenderer(),
        _ => new PixmanRenderer(),
    };

    private static int Run(
        BasinLogger log,
        long frames,
        string? clientCommand,
        string? screenshotPath,
        TransportChoice transport,
        string rendererName,
        string? channelEndpoint,
        bool gpu,
        string video,
        string compress,
        IpcChoice ipcChoice,
        out long renderedFrames)
    {
        BasinCounters.Reset();
        var videoCodec = video.Split(',')[0];
        Basin.Capabilities.IVideoDecoder? decoder = null;
        if (videoCodec != "none")
        {
            gpu = true;
            decoder = Basin.Video.FFmpeg.FFmpegVideoDecoder.TryCreate(
                video.EndsWith(",hw", StringComparison.Ordinal), out var whyNot);
            if (decoder is null)
            {
                log.Error($"--video {video} needs a decoder and none is available: {whyNot}");
                renderedFrames = 0;
                return 1;
            }
        }

        using var host = Basin.Host.BasinHost.Create(new Basin.Host.HostOptions
        {
            Transport = channelEndpoint is not null || transport.Kind == TransportKind.Managed
                ? Basin.Host.HostTransport.Managed
                : Basin.Host.HostTransport.LibWayland,
        });
        var display = host.Display;
        var socket = host.Socket;
        var loop = host.Loop;
        var buffers = new ClientBufferRegistry();
        _ = new ShmGlobal(display, buffers: buffers);
        using var compositorGlobal = new CompositorGlobal(display, buffers);
        using var subcompositorGlobal = new SubcompositorGlobal(display, compositorGlobal);
        using var seatGlobal = new SeatGlobal(display);
        var output = host.Headless!.CreateOutput(new OutputMode(800, 600, 60_000));
        using var outputGlobal = new OutputGlobal(display, output);
        using var renderer = CreateRenderer(rendererName);
        var scene = new Scene.Scene();
        var target = new MemoryBuffer(800, 600, DrmFormat.Xrgb8888);
        var frameState = new OutputState();
        var sceneSurfaces = new List<SceneSurface>();
        var shell = new XdgShell(display, compositorGlobal);
        using var adoption = new Basin.Transport.Waypipe.WaypipeAdoption(display, buffers, compositorGlobal, hostHasDmabuf: false);

        var placement = 40;
        shell.ToplevelMapped += surface =>
        {
            var sceneSurface = new SceneSurface(scene.Root, surface);
            sceneSurface.Tree.SetPosition(placement, placement);
            placement += 60;
            sceneSurfaces.Add(sceneSurface);
            sceneSurface.Destroyed += () => sceneSurfaces.Remove(sceneSurface);
        };

        long rendered = 0;
        var running = true;
        Basin.Transport.Waypipe.WaypipeListener? listener = null;
        output.Frame += () =>
        {
            scene.Render(renderer, target, new RenderColor(0.06f, 0.06f, 0.08f, 1f));
            frameState.Clear();
            output.Commit(frameState.SetBuffer(target));
            var timestamp = (uint)Environment.TickCount;
            foreach (var sceneSurface in sceneSurfaces)
            {
                sceneSurface.SendFrameDone(timestamp);
            }

            if (listener is null || listener.Accepted > 0)
            {
                rendered++;
            }
        };

        if (socket.Length > 0)
        {
            log.Info($"listening on {socket} (800x600@60, software). WAYLAND_DISPLAY={socket} <client>");
        }
        else
        {
            log.Info($"no local socket on this host (800x600@60, software); a client arrives over a channel");
        }

        using var ipcServices = new BasinServices(loop).Freeze();
        using var ipc = ipcChoice.Attach(loop, ipcServices, socket, new Basin.Ipc.IpcSessionInfo
        {
            Compositor = "basin-headless",
            Backend = "headless",
            Renderer = rendererName,
            Quit = () => running = false,
        });
        new Basin.Ipc.IpcLineReport().AddLine(ipc.Methods, Basin.Ipc.IpcMethodNames.SessionQuit, "quit");
        ipc.Start();
        ipc.StartLineFront();
        using var processes = new ProcessTracker(loop, () => ProcessTracker.SessionEnvironment(socket, null, ipc.Path));
        var client = clientCommand is null ? null : processes.SpawnShell(clientCommand, out _);
        if (clientCommand is not null && client is null)
        {
            throw new InvalidOperationException($"failed to start '{clientCommand}'");
        }

        if (channelEndpoint is not null)
        {
            var endpoint = Basin.Transport.Waypipe.WaypipeEndpoint.ForListening(channelEndpoint);
            listener = Basin.Transport.Waypipe.WaypipeListener.Bind(
                loop,
                endpoint,
                compress switch
                {
                    "none" => Basin.Transport.Waypipe.WaypipeCompression.None,
                    "zstd" => Basin.Transport.Waypipe.WaypipeCompression.Zstd,
                    _ => Basin.Transport.Waypipe.WaypipeCompression.Lz4,
                },
                options: new Basin.Transport.Waypipe.WaypipeChannelOptions
                {
                    CarriesDmabuf = gpu,
                    AcceptsVideo = videoCodec != "none",
                    VideoDecoder = decoder,
                });
            listener.ChannelAccepted += accepted =>
            {
                var index = listener.Accepted;
                accepted.Ended += failure => log.Info($"channel {index} ended{(failure is null ? string.Empty : $": {failure.Message}")}");
                adoption.Adopt(accepted);
                log.Info($"channel {index} attached; replaying it as one client");
            };
            listener.Failed += failure =>
            {
                log.Error($"the channel listener failed: {failure.Message}");
                running = false;
            };
            log.Info($"accepting waypipe channels on {listener.LocalEndPoint}, replayed over the managed transport");
        }

        var interrupt = loop.AddSignal(Signal.Interrupt, _ => running = false);
        var terminate = loop.AddSignal(Signal.Terminate, _ => running = false);

        while (running && (frames == 0 || rendered < frames))
        {
            loop.Dispatch(frames == 0 ? -1 : 16);
            if (client is { IsRunning: false })
            {
                log.Error($"client exited prematurely with code {client.ExitCode}");
                renderedFrames = rendered;
                return 1;
            }
        }

        renderedFrames = rendered;

        if (screenshotPath is not null)
        {
            BufferCapture.WritePng(target, screenshotPath);
            log.Info($"screenshot written to {screenshotPath} after {rendered} frames");
        }

        if (listener is not null)
        {
            listener.Dispose();
            var drained = Stopwatch.StartNew();
            while (buffers.Count > 0 && drained.ElapsedMilliseconds < 2000)
            {
                loop.Dispatch(50);
            }
        }

        if (client is not null)
        {
            processes.TerminateAll(TimeSpan.FromSeconds(2));

            var drain = Stopwatch.StartNew();
            while (compositorGlobal.Surfaces.Count > 0 && drain.ElapsedMilliseconds < 2000)
            {
                loop.Dispatch(50);
            }
        }

        host.DisconnectClients();
        adoption.Dispose();
        processes.Dispose();
        interrupt.Remove();
        terminate.Remove();
        ipc?.Dispose();
        ipcServices.Dispose();
        output.Destroy();
        target.Destroy();
        scene.Root.Destroy();
        frameState.Dispose();

        BasinReport.Line(CompositorLines.Frames(rendered));

        if (frames > 0)
        {
            if (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0 || buffers.Count != 0)
            {
                log.Error($"teardown not clean (live={BasinCounters.LiveObjects} pendingFrees={BasinCounters.PendingFrees} buffers={buffers.Count})");
                return 1;
            }

            if (BasinCounters.Enabled)
            {
                log.Info($"OK: {rendered} frames, teardown clean");
            }
            else
            {
                log.Info($"OK: {rendered} frames, buffers released (lifetime tracking compiled out)");
            }
        }

        return 0;
    }
}
