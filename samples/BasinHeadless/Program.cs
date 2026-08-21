using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Microsoft.Extensions.Logging;
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

        return cli.Run(args, result =>
        {
            using var loggers = cli.CreateLoggerFactory(result);
            if (result.GetValue(client) is not null && result.GetValue(channel) is not null)
            {
                Console.Error.WriteLine("--client spawns a local client and --waypipe-listen waits for a remote one");
                return 1;
            }

            if (result.GetValue(channel) is not null &&
                result.GetValue(transport).Kind == TransportKind.LibWayland &&
                result.GetResult(transport) is { Implicit: false })
            {
                Console.Error.WriteLine("--waypipe-listen replays its channel over the managed transport");
                return 1;
            }

            var status = Run(
                loggers.CreateLogger("BasinHeadless"),
                result.GetValue(frames),
                result.GetValue(client),
                result.GetValue(screenshot),
                result.GetValue(transport),
                result.GetValue(renderer)!,
                result.GetValue(channel),
                result.GetValue(gpu),
                result.GetValue(video)!,
                result.GetValue(compress)!,
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
        ILogger log,
        long frames,
        string? clientCommand,
        string? screenshotPath,
        TransportChoice transport,
        string rendererName,
        string? channelEndpoint,
        bool gpu,
        string video,
        string compress,
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
                log.LogError("--video {Codec} needs a decoder and none is available: {Reason}", video, whyNot);
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
        using var dmabufGlobal = gpu
            ? new LinuxDmabufGlobal(
                display,
                buffers,
                new Basin.Transport.Waypipe.WaypipeGlobals(gpu, decoder is not null).Formats,
                Basin.Transport.Waypipe.WaypipeGlobals.SyntheticMainDevice,
                compositor: compositorGlobal)
            : null;

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

            rendered++;
        };

        if (socket.Length > 0)
        {
            log.LogInformation(
                "listening on {Socket} (800x600@60, software). WAYLAND_DISPLAY={Socket} <client>", socket, socket);
        }
        else
        {
            log.LogInformation("no local socket on this host (800x600@60, software); a client arrives over a channel");
        }

        var client = BasinDiagnostics.StartClient(clientCommand, socket);
        if (clientCommand is not null && client is null)
        {
            throw new InvalidOperationException($"failed to start '{clientCommand}'");
        }

        Basin.Transport.Waypipe.WaypipeChannel? channel = null;
        if (channelEndpoint is not null)
        {
            System.Net.EndPoint endpoint = channelEndpoint.Contains(':', StringComparison.Ordinal)
                ? System.Net.IPEndPoint.Parse(channelEndpoint)
                : new System.Net.Sockets.UnixDomainSocketEndPoint(channelEndpoint);
            if (endpoint is System.Net.Sockets.UnixDomainSocketEndPoint && File.Exists(channelEndpoint))
            {
                File.Delete(channelEndpoint);
            }

            log.LogInformation(
                "waiting for a waypipe channel on {Endpoint}, replayed over the managed transport", endpoint);
            channel = Basin.Transport.Waypipe.WaypipeChannel.Listen(
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
            channel.Ended += failure => log.LogInformation(
                "channel ended{Reason}", failure is null ? string.Empty : $": {failure.Message}");
            display.CreateClient(channel.Transport);
            log.LogInformation("channel attached; replaying it as one client");
        }

        var interrupt = loop.AddSignal(Signal.Interrupt, _ => running = false);
        var terminate = loop.AddSignal(Signal.Terminate, _ => running = false);

        while (running && (frames == 0 || rendered < frames))
        {
            loop.Dispatch(frames == 0 ? -1 : 16);
            if (client is not null && client.HasExited)
            {
                log.LogError("client exited prematurely with code {Code}", client.ExitCode);
                renderedFrames = rendered;
                return 1;
            }
        }

        renderedFrames = rendered;

        if (screenshotPath is not null)
        {
            BufferCapture.WritePng(target, screenshotPath);
            log.LogInformation("screenshot written to {Path} after {Count} frames", screenshotPath, rendered);
        }

        if (channel is not null)
        {
            channel.Dispose();
            var drained = Stopwatch.StartNew();
            while (buffers.Count > 0 && drained.ElapsedMilliseconds < 2000)
            {
                loop.Dispatch(50);
            }
        }

        if (client is not null)
        {
            BasinDiagnostics.StopClient(client);

            var drain = Stopwatch.StartNew();
            while (compositorGlobal.Surfaces.Count > 0 && drain.ElapsedMilliseconds < 2000)
            {
                loop.Dispatch(50);
            }
        }

        interrupt.Remove();
        terminate.Remove();
        output.Destroy();
        target.Destroy();
        scene.Root.Destroy();
        frameState.Dispose();

        Console.WriteLine(
            $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");

        if (frames > 0)
        {
            if (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0 || buffers.Count != 0)
            {
                log.LogError(
                    "teardown not clean (live={Live} pendingFrees={PendingFrees} buffers={Buffers})",
                    BasinCounters.LiveObjects, BasinCounters.PendingFrees, buffers.Count);
                return 1;
            }

            log.LogInformation(BasinCounters.Enabled
                ? "OK: {Count} frames, teardown clean"
                : "OK: {Count} frames, buffers released (lifetime tracking compiled out)", rendered);
        }

        return 0;
    }
}
