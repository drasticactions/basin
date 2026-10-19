using System.Text;
using Basin;
using Basin.Capabilities;
using Basin.Sdl;
using SDL3;
using Wayland;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlClipboardTests
{
    private static void Focus(SdlCompHarness harness)
    {
        harness.MapToplevel(200, 150, (data, stride) => { });
        SdlEvents.Window(harness.Comp.Window.Window.Id, SDL.EventType.WindowFocusGained);
        harness.Pump();
    }

    [Fact]
    public void The_offscreen_driver_keeps_a_clipboard()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        Assert.True(SDL.SetClipboardText("probe"));
        Assert.Equal("probe", SDL.GetClipboardText());
    }

    [Fact]
    public void A_client_selection_reaches_the_host_clipboard_in_every_offered_type()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        Focus(harness);
        var client = harness.Client;
        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        var source = client.DataDeviceManager.CreateDataSource();
        source.Offer("text/plain;charset=utf-8");
        source.Offer("application/x-basin-test");
        source.Send += (_, e) => Pipes.WriteText(e.Fd, e.MimeType == "application/x-basin-test" ? "custom bytes" : "copied in the client");
        device.SetSelection(source, 0);
        harness.PumpUntil(() => harness.Comp.Clipboard.OwnsHostClipboard, "the client selection never reached SDL");
        for (var i = 0; i < 20; i++)
        {
            harness.Pump();
        }

        Assert.True(SDL.HasClipboardData("application/x-basin-test"));
        Assert.Equal("copied in the client", HostData("text/plain;charset=utf-8"));
        Assert.Equal("custom bytes", HostData("application/x-basin-test"));
    }

    private static unsafe string? HostData(string mime)
    {
        var pointer = SDL.GetClipboardData(mime, out var size);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString((byte*)pointer, (int)size);
        }
        finally
        {
            SDL.Free(pointer);
        }
    }

    [Fact]
    public void The_host_clipboard_reaches_a_focused_client()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var client = harness.Client;
        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        WlDataOffer? offer = null;
        var mimes = new List<string>();
        device.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, o) => mimes.Add(o.MimeType);
        };
        var selected = false;
        device.Selection += (_, e) => selected = e.Id is not null;
        Focus(harness);

        Assert.True(SDL.SetClipboardText("typed on the host"));
        harness.PumpUntil(() => selected && mimes.Contains("text/plain;charset=utf-8"), "the client never saw the host selection");

        var (readFd, writeFd) = Pipes.Open();
        offer!.Receive("text/plain;charset=utf-8", writeFd);
        client.Display.Flush();
        Pipes.close(writeFd);
        Assert.Equal("typed on the host", Pipes.ReadText(readFd, harness.Pump));
    }

    [Fact]
    public void An_emptied_host_clipboard_clears_the_selection_it_gave_the_guest()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var store = harness.Host.Services.Require<ISelectionStore>();
        Assert.True(SDL.SetClipboardText("short lived"));
        harness.PumpUntil(() => store.Current(SelectionKind.Clipboard) is not null, "the host text never reached the store");

        Assert.True(SDL.ClearClipboardData());
        harness.PumpUntil(() => store.Current(SelectionKind.Clipboard) is null, "the stale host selection stayed in the guest");
    }

    [Fact]
    public void Primary_selection_text_crosses_both_ways()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var store = harness.Host.Services.Require<ISelectionStore>();
        var bytes = Encoding.UTF8.GetBytes("selected in the guest");
        var guest = new DataSource(["text/plain;charset=utf-8"], (mime, fd) => Task.Run(() =>
        {
            unsafe
            {
                fixed (byte* data = bytes)
                {
                    _ = Write(fd.Value, data, (nuint)bytes.Length);
                }
            }

            fd.Close();
        }));
        Assert.True(store.SetSelection(SelectionKind.Primary, guest, SelectionSerial.Unchecked));
        harness.PumpUntil(() => SDL.GetPrimarySelectionText() == "selected in the guest", "the guest primary never reached SDL");

        Assert.True(SDL.SetPrimarySelectionText("selected on the host"));
        SdlEvents.Window(harness.Comp.Window.Window.Id, SDL.EventType.WindowFocusGained);
        harness.PumpUntil(() => store.Current(SelectionKind.Primary) is { } current && !ReferenceEquals(current, guest), "the host primary never reached the store");
        var (readFd, writeFd) = Pipes.Open();
        Assert.True(store.Receive(SelectionKind.Primary, "text/plain;charset=utf-8", new ClientFd(writeFd, null)));
        Assert.Equal("selected on the host", Pipes.ReadText(readFd, harness.Pump));
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "write")]
    private static extern unsafe nint Write(int fd, byte* buffer, nuint count);
}
