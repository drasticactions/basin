using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Portal.Client;
using Basin.Portal.Client.Protocol;
using Wayland;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class DataControlStoreTests
{
    [DllImport("libc")]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc")]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    private static ExtDataControlManagerV1 Bind(CompositorTestHost host, ShmTestClient client)
    {
        ExtDataControlManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_data_control_manager_v1")
            {
                proxy = registry.Bind<ExtDataControlManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static int LiveServerObjects(WlClient client)
    {
        var live = 0;
        for (var id = 0xFF000000u; id < 0xFF000040u; id++)
        {
            if (client.GetObject(id) is not null)
            {
                live++;
            }
        }

        return live;
    }

    private sealed class Writer
    {
        private readonly ExtDataControlManagerV1 _manager;
        private readonly ExtDataControlDeviceV1 _device;
        private readonly List<ExtDataControlSourceV1> _sources = [];

        public Writer(CompositorTestHost host)
        {
            var client = host.ConnectClient();
            _manager = Bind(host, client);
            _device = _manager.GetDataDevice(client.Seat!);
        }

        public void Set(bool primary, string? text)
        {
            if (text is null)
            {
                _device.SetSelection(null);
                return;
            }

            var source = _manager.CreateDataSource();
            _sources.Add(source);
            source.Offer(primary ? "text/x-highlight" : "text/plain");
            source.Offer("text/plain;charset=utf-8");
            source.Send += (_, e) => Write(e.Fd, text);
            if (primary)
            {
                _device.SetPrimarySelection(source);
            }
            else
            {
                _device.SetSelection(source);
            }
        }

        private static unsafe void Write(int fd, string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            fixed (byte* p = bytes)
            {
                write(fd, p, (nuint)bytes.Length);
            }

            close(fd);
        }
    }

    private static unsafe string Read(CompositorTestHost host, DataControlSelectionStore store)
    {
        var fds = stackalloc int[2];
        Assert.Equal(0, pipe(fds));
        Assert.True(store.Receive(SelectionKind.Clipboard, "text/plain;charset=utf-8", new ClientFd(fds[1], null)));
        host.PumpToClient();
        host.PumpToClient();
        var buffer = stackalloc byte[64];
        var got = read(fds[0], buffer, 64);
        close(fds[0]);
        return System.Text.Encoding.UTF8.GetString(buffer, (int)got);
    }

    [Fact]
    public void The_store_serves_the_clipboard_and_never_the_primary_selection()
    {
        using var host = new CompositorTestHost();
        using var manager = new ExtDataControlManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat));
        using var primary = new PrimarySelectionManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat), host.Seat);
        var writer = new Writer(host);

        var portal = host.ConnectClient();
        var serverClient = host.Display.Clients[^1];
        using var store = new DataControlSelectionStore(Bind(host, portal), portal.Seat!);
        var changes = 0;
        store.SelectionChanged += _ => changes++;
        host.PumpToClient();
        Assert.Equal(0, store.GetOffer(SelectionKind.Clipboard, new string[4]));

        writer.Set(primary: false, "copy");
        host.PumpUntil(() => changes >= 2);
        writer.Set(primary: true, "highlight");
        host.PumpToClient();
        host.PumpToClient();

        var types = new string[4];
        Assert.Equal(2, store.GetOffer(SelectionKind.Clipboard, types));
        Assert.Equal(["text/plain", "text/plain;charset=utf-8"], types[..2]);
        Assert.Equal("copy", Read(host, store));
        Assert.Equal(2, LiveServerObjects(serverClient));

        writer.Set(primary: true, "highlight again");
        host.PumpToClient();
        host.PumpToClient();
        Assert.Equal(2, LiveServerObjects(serverClient));
        Assert.Equal("copy", Read(host, store));

        writer.Set(primary: false, null);
        host.PumpUntil(() => store.GetOffer(SelectionKind.Clipboard, types) == 0);
        host.PumpToClient();
        Assert.Equal(1, LiveServerObjects(serverClient));
    }
}
