using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Tests;

internal sealed class MappedToplevel
{
    public required WlSurface Surface;
    public required XdgSurface XdgSurface;
    public required XdgToplevel Toplevel;
    public required XdgToplevelWindow ServerToplevel;
    public required ClientShmBuffer Buffer;
    public uint LastConfigureSerial;
    public int ConfiguredWidth;
    public int ConfiguredHeight;
    public bool CloseReceived;

    public readonly List<uint> ConfiguredStates = [];

    public Surface ServerSurface => ServerToplevel.Surface;

    public static MappedToplevel Map(
        CompositorTestHost host,
        ShmTestClient client,
        int width = 60,
        int height = 50,
        uint color = 0xFF336699,
        XdgWmBase? wmBase = null,
        Action<XdgToplevel>? beforeMap = null)
    {
        XdgToplevelWindow? serverToplevel = null;
        void Capture(XdgToplevelWindow toplevel) => serverToplevel ??= toplevel;
        host.Shell.NewToplevel += Capture;

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = (wmBase ?? client.WmBase!).GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetAppId("basin-test");

        beforeMap?.Invoke(toplevel);

        var result = new MappedToplevel
        {
            Surface = surface,
            XdgSurface = xdgSurface,
            Toplevel = toplevel,
            ServerToplevel = null!,
            Buffer = client.CreateBuffer(width, height, Fill.Solid(width, height, color)),
        };

        toplevel.Configure += (_, e) =>
        {
            (result.ConfiguredWidth, result.ConfiguredHeight) = (e.Width, e.Height);
            result.ConfiguredStates.Clear();
            for (var offset = 0; offset + 4 <= e.States.Length; offset += 4)
            {
                result.ConfiguredStates.Add(BitConverter.ToUInt32(e.States, offset));
            }
        };
        toplevel.Close += (_, _) => result.CloseReceived = true;
        var configured = false;
        xdgSurface.Configure += (_, e) =>
        {
            result.LastConfigureSerial = e.Serial;
            xdgSurface.AckConfigure(e.Serial);
            configured = true;
        };

        surface.Commit();
        host.PumpUntil(() => configured);

        surface.Attach(result.Buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        host.PumpUntil(() => serverToplevel is { IsMapped: true });

        host.Shell.NewToplevel -= Capture;
        result.ServerToplevel = serverToplevel!;
        return result;
    }
}
