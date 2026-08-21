using System.Runtime.InteropServices;
using Basin.Cli;
using BlurClient.Protocol;
using Microsoft.Extensions.Logging;
using Wayland;

namespace BlurClient;

internal static class Program
{
    private const uint BodyColor = 0x5F20242C;
    private const int Frame = 12;
    private const uint ButtonLeft = 0x110;

    private static int Main(string[] args)
    {
        var cli = new BasinCommand("Test program for checking blur effects.");
        var socketOption = cli.Add(CommonOptions.Socket());
        var widthOption = cli.Add(CommonOptions.Width(480));
        var heightOption = cli.Add(CommonOptions.Height(320));

        return cli.Run(args, result =>
        {
            using var loggers = cli.CreateLoggerFactory(result);
            return Run(
                loggers.CreateLogger("BlurClient"),
                result.GetValue(socketOption),
                result.GetValue(widthOption),
                result.GetValue(heightOption));
        });
    }

    private static int Run(ILogger log, string? socket, int width, int height)
    {
        using var display = socket is null ? WlDisplay.Connect() : WlDisplay.Connect(socket);
        var registry = display.GetRegistry();

        WlCompositor? compositor = null;
        WlShm? shm = null;
        XdgWmBase? wmBase = null;
        WlSeat? seat = null;
        var hasPointer = false;
        ExtBackgroundEffectManagerV1? effects = null;
        uint capabilities = 0;
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
                case "xdg_wm_base":
                    wmBase = registry.Bind<XdgWmBase>(e.Name, 1);
                    break;
                case "wl_seat":
                    seat = registry.Bind<WlSeat>(e.Name, Math.Min(5u, e.Version));
                    seat.Capabilities += (_, ce) => hasPointer = (ce.Capabilities & WlSeat.Capability.Pointer) != 0;
                    break;
                case "ext_background_effect_manager_v1":
                    effects = registry.Bind<ExtBackgroundEffectManagerV1>(e.Name, 1);
                    effects.Capabilities += (_, ce) => capabilities = (uint)ce.Flags;
                    break;
            }
        };
        display.Roundtrip();

        if (compositor is null || shm is null || wmBase is null)
        {
            log.LogError("compositor is missing wl_compositor, wl_shm or xdg_wm_base");
            return 1;
        }

        if (effects is null)
        {
            log.LogWarning("ext_background_effect_manager_v1 is not advertised; the window will be plain translucent");
        }

        wmBase.Ping += (_, e) => wmBase.Pong(e.Serial);

        var surface = compositor.CreateSurface();
        var xdgSurface = wmBase.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetTitle("BlurClient");
        toplevel.SetAppId("basin-blur-client");
        toplevel.SetMinSize(Frame * 8, Frame * 8);

        if (effects is not null)
        {
            var effect = effects.GetBackgroundEffect(surface);
            var region = compositor.CreateRegion();

            region.Add(0, 0, 1 << 24, 1 << 24);
            effect.SetBlurRegion(region);
            region.Destroy();
            display.Roundtrip();
            Console.WriteLine($"CAPABILITIES {capabilities} (1 = blur promised)");
        }

        var closed = false;
        var drawn = false;
        var needRedraw = false;
        var pendingWidth = width;
        var pendingHeight = height;
        toplevel.Close += (_, _) => closed = true;
        toplevel.Configure += (_, e) =>
        {
            if (e.Width > 0 && e.Height > 0)
            {
                (pendingWidth, pendingHeight) = (e.Width, e.Height);
            }
        };
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            if (!drawn || pendingWidth != width || pendingHeight != height)
            {
                (width, height) = (pendingWidth, pendingHeight);
                needRedraw = true;
            }
        };

        if (hasPointer && seat is not null)
        {
            var pointer = seat.GetPointer();
            double pointerX = 0, pointerY = 0;
            pointer.Enter += (_, e) => (pointerX, pointerY) = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
            pointer.Motion += (_, e) => (pointerX, pointerY) = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
            pointer.Button += (_, e) =>
            {
                if (e.State != WlPointer.ButtonState.Pressed || e.Button != ButtonLeft)
                {
                    return;
                }

                var edges = EdgesAt(pointerX, pointerY, width, height);
                if (edges == XdgToplevel.ResizeEdge.None)
                {
                    toplevel.Move(seat, e.Serial);
                }
                else
                {
                    toplevel.Resize(seat, e.Serial, edges);
                }
            };
        }

        surface.Commit();
        display.Flush();

        ShmBuffer? shown = null;
        while (!closed)
        {
            display.Dispatch();
            if (!needRedraw || closed)
            {
                continue;
            }

            needRedraw = false;
            var buffer = new ShmBuffer(shm, width, height);
            Paint(buffer);
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, width, height);
            surface.Commit();
            display.Flush();

            buffer.Proxy.Release += (_, _) => buffer.Dispose();
            shown = buffer;
            if (!drawn)
            {
                drawn = true;
                Console.WriteLine($"MAPPED {width}x{height}");
            }
        }

        shown?.Dispose();
        return 0;
    }

    private static XdgToplevel.ResizeEdge EdgesAt(double x, double y, int width, int height)
    {
        var edges = XdgToplevel.ResizeEdge.None;
        if (y < Frame)
        {
            edges |= XdgToplevel.ResizeEdge.Top;
        }
        else if (y >= height - Frame)
        {
            edges |= XdgToplevel.ResizeEdge.Bottom;
        }

        if (x < Frame)
        {
            edges |= XdgToplevel.ResizeEdge.Left;
        }
        else if (x >= width - Frame)
        {
            edges |= XdgToplevel.ResizeEdge.Right;
        }

        return edges;
    }

    private static unsafe void Paint(ShmBuffer buffer)
    {
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(buffer.Data + y * buffer.Stride);
            for (var x = 0; x < buffer.Width; x++)
            {
                var onFrame = x < Frame || y < Frame || x >= buffer.Width - Frame || y >= buffer.Height - Frame;
                row[x] = onFrame ? 0xFF10131A : BodyColor;
            }
        }
    }
}
