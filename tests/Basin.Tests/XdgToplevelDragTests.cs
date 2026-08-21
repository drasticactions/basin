using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class XdgToplevelDragTests
{
    [Fact]
    public void The_attached_toplevel_and_its_offset_reach_the_consumer()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgToplevelDragManager(host.Display, host.Seat.DataDevice);

        var origin = MappedToplevel.Map(host, host.Client, color: 0xFF101010);
        var dragged = MappedToplevel.Map(host, host.Client, color: 0xFF202020);
        var changes = new List<ToplevelDragAttachment?>();
        manager.AttachmentChanged += attachment => changes.Add(attachment);

        var proxy = BindManager(host, host.Client);
        var serial = PressOn(host, host.Client, origin);

        var device = host.Client.DataDeviceManager!.GetDataDevice(host.Client.Seat!);
        var source = host.Client.DataDeviceManager.CreateDataSource();
        source.Offer("text/uri-list");
        var drag = proxy.GetXdgToplevelDrag(source);
        device.StartDrag(source, origin.Surface, null, serial);
        drag.Attach(dragged.Toplevel, 12, 34);
        host.PumpUntil(() => manager.Attachment is not null);

        var attachment = manager.Attachment!.Value;
        Assert.Same(dragged.ServerToplevel, attachment.Toplevel);
        Assert.Equal(12, attachment.OffsetX);
        Assert.Equal(34, attachment.OffsetY);
        Assert.Equal(attachment, Assert.Single(changes));

        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => manager.Attachment is null);
        Assert.Null(changes[^1]);

        drag.Dispose();
        source.Dispose();
        device.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Unmapping_the_attached_toplevel_detaches_it_without_an_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgToplevelDragManager(host.Display, host.Seat.DataDevice);

        var origin = MappedToplevel.Map(host, host.Client, color: 0xFF101010);
        var dragged = MappedToplevel.Map(host, host.Client, color: 0xFF202020);
        var proxy = BindManager(host, host.Client);
        var serial = PressOn(host, host.Client, origin);

        var device = host.Client.DataDeviceManager!.GetDataDevice(host.Client.Seat!);
        var source = host.Client.DataDeviceManager.CreateDataSource();
        var drag = proxy.GetXdgToplevelDrag(source);
        device.StartDrag(source, origin.Surface, null, serial);
        drag.Attach(dragged.Toplevel, 0, 0);
        host.PumpUntil(() => manager.Attachment is not null);

        dragged.Surface.Attach(null, 0, 0);
        dragged.Surface.Commit();
        host.PumpUntil(() => manager.Attachment is null);

        dragged.Surface.Attach(dragged.Buffer.Proxy, 0, 0);
        dragged.Surface.Commit();
        host.PumpToServer();
        drag.Attach(dragged.Toplevel, 5, 6);
        host.PumpUntil(() => manager.Attachment is not null);
        Assert.Equal(5, manager.Attachment!.Value.OffsetX);

        drag.Dispose();
        source.Dispose();
        device.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_second_toplevel_raises_toplevel_attached()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgToplevelDragManager(host.Display, host.Seat.DataDevice);
        var client = host.ConnectClient();

        var origin = MappedToplevel.Map(host, client, color: 0xFF101010);
        var first = MappedToplevel.Map(host, client, color: 0xFF202020);
        var second = MappedToplevel.Map(host, client, color: 0xFF303030);
        var proxy = BindManager(host, client);
        var serial = PressOn(host, client, origin);

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        var source = client.DataDeviceManager.CreateDataSource();
        var drag = proxy.GetXdgToplevelDrag(source);
        device.StartDrag(source, origin.Surface, null, serial);
        drag.Attach(first.Toplevel, 0, 0);
        host.PumpUntil(() => manager.Attachment is not null);
        Assert.Same(first.ServerToplevel, manager.Attachment!.Value.Toplevel);

        drag.Attach(second.Toplevel, 0, 0);
        var error = ExpectError(host);
        Assert.Equal((int)Basin.Shell.Xdg.Protocol.XdgToplevelDragV1.Error.ToplevelAttached, error.ErrorCode);
        Assert.Equal("xdg_toplevel_drag_v1", error.InterfaceName);
        host.DisconnectClient(client);
    }

    [Fact]
    public void A_second_drag_object_for_one_source_raises_invalid_source()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgToplevelDragManager(host.Display, host.Seat.DataDevice);
        var client = host.ConnectClient();
        var proxy = BindManager(host, client);

        var source = client.DataDeviceManager!.CreateDataSource();
        proxy.GetXdgToplevelDrag(source);
        proxy.GetXdgToplevelDrag(source);

        var error = ExpectError(host);
        Assert.Equal((int)Basin.Shell.Xdg.Protocol.XdgToplevelDragManagerV1.Error.InvalidSource, error.ErrorCode);
        Assert.Equal("xdg_toplevel_drag_manager_v1", error.InterfaceName);
        host.DisconnectClient(client);
    }

    [Fact]
    public void Destroying_the_object_mid_drag_raises_ongoing_drag()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgToplevelDragManager(host.Display, host.Seat.DataDevice);
        var client = host.ConnectClient();

        var origin = MappedToplevel.Map(host, client, color: 0xFF101010);
        var proxy = BindManager(host, client);
        var serial = PressOn(host, client, origin);

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        var source = client.DataDeviceManager.CreateDataSource();
        var drag = proxy.GetXdgToplevelDrag(source);
        device.StartDrag(source, origin.Surface, null, serial);
        host.PumpUntil(() => host.Seat.DataDevice.DraggingSource is not null);

        drag.Dispose();
        var error = ExpectError(host);
        Assert.Equal((int)Basin.Shell.Xdg.Protocol.XdgToplevelDragV1.Error.OngoingDrag, error.ErrorCode);
        host.DisconnectClient(client);
    }

    [Fact]
    public void Destroying_the_object_after_the_drag_ended_is_allowed()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgToplevelDragManager(host.Display, host.Seat.DataDevice);

        var origin = MappedToplevel.Map(host, host.Client, color: 0xFF101010);
        var proxy = BindManager(host, host.Client);
        var serial = PressOn(host, host.Client, origin);

        var device = host.Client.DataDeviceManager!.GetDataDevice(host.Client.Seat!);
        var source = host.Client.DataDeviceManager.CreateDataSource();
        var drag = proxy.GetXdgToplevelDrag(source);
        device.StartDrag(source, origin.Surface, null, serial);
        host.PumpUntil(() => host.Seat.DataDevice.DraggingSource is not null);

        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => host.Seat.DataDevice.DraggingSource is null);

        drag.Dispose();
        host.PumpToClient();
        Assert.Null(manager.Attachment);

        source.Dispose();
        device.Dispose();
        host.PumpToServer();
    }

    private static Basin.Shell.Xdg.Protocol.XdgToplevelDragManagerV1 BindManager(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Shell.Xdg.Protocol.XdgToplevelDragManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "xdg_toplevel_drag_manager_v1")
            {
                proxy = registry.Bind<Basin.Shell.Xdg.Protocol.XdgToplevelDragManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static WaylandProtocolException ExpectError(CompositorTestHost host)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                host.PumpToClient();
            }
            catch (WaylandProtocolException error)
            {
                return error;
            }
        }

        throw new TimeoutException("no protocol error arrived while pumping");
    }

    private static uint PressOn(CompositorTestHost host, ShmTestClient client, MappedToplevel window)
    {
        var pointer = client.Seat!.GetPointer();
        uint serial = 0;
        pointer.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                serial = e.Serial;
            }
        };
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => serial != 0);
        return serial;
    }
}
