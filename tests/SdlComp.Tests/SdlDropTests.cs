using Basin.Sdl;
using SDL3;
using Wayland;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlDropTests
{
    [Fact]
    public void A_file_dropped_from_the_host_reaches_a_client_as_a_uri_list()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var client = harness.Client;
        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        WlDataOffer? offer = null;
        var mimes = new List<string>();
        var entered = false;
        var dropped = false;
        device.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, o) => mimes.Add(o.MimeType);
        };
        device.Enter += (_, e) =>
        {
            entered = true;
            e.Id?.Accept(e.Serial, SdlDrop.UriListMime);
            e.Id?.SetActions(WlDataDeviceManager.DndAction.Copy, WlDataDeviceManager.DndAction.Copy);
        };
        device.Drop += (_, _) => dropped = true;
        harness.MapToplevel(200, 150, (data, stride) => { });
        var box = harness.Comp.Shell.Windows[0].ClientBox;
        var id = harness.Comp.Window.Window.Id;

        SdlEvents.Drop(id, SDL.EventType.DropBegin);
        SdlEvents.Drop(id, SDL.EventType.DropPosition, box.X + 30, box.Y + 30);
        harness.PumpUntil(() => entered && mimes.Contains(SdlDrop.UriListMime), "the drag never entered the client");
        Assert.True(harness.Comp.Drop.IsDragging);

        SdlEvents.Drop(id, SDL.EventType.DropPosition, box.X + 60, box.Y + 40);
        SdlEvents.Drop(id, SDL.EventType.DropFile, data: "/tmp/basin dropped.txt");
        SdlEvents.Drop(id, SDL.EventType.DropComplete);
        harness.PumpUntil(() => dropped, "the client never saw the drop");

        var (readFd, writeFd) = Pipes.Open();
        offer!.Receive(SdlDrop.UriListMime, writeFd);
        client.Display.Flush();
        Pipes.close(writeFd);
        Assert.Equal("file:///tmp/basin%20dropped.txt\r\n", Pipes.ReadText(readFd, harness.Pump));
        Assert.False(harness.Comp.Drop.IsDragging);
        Assert.Equal(1, harness.Comp.Drop.Drops);
    }

    [Fact]
    public void A_drag_that_leaves_with_no_data_is_cancelled()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Drop(id, SDL.EventType.DropBegin);
        SdlEvents.Drop(id, SDL.EventType.DropPosition, 30, 30);
        harness.PumpUntil(() => harness.Comp.Drop.IsDragging, "the drag never started");
        SdlEvents.Drop(id, SDL.EventType.DropComplete);
        harness.PumpUntil(() => !harness.Comp.Drop.IsDragging, "the drag never ended");
        Assert.Equal(0, harness.Comp.Drop.Drops);
        Assert.Null(harness.Host.Seat.DataDevice.DraggingSource);
    }
}
