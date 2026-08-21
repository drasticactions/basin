using System.Runtime.InteropServices;
using System.Text;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public class NestedSeamTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, void* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, void* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void The_host_clipboard_reaches_a_guest()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Selecting);
        var output = host.CreateOutput();
        var store = new RecordingSelectionStore();
        using var seam = new NestedSeam(host.Backend, store);

        OfferOnParent(host, "text/plain", "from-the-host");

        Assert.Contains(SelectionKind.Clipboard, store.Changes);
        var source = store.Current(SelectionKind.Clipboard);
        Assert.NotNull(source);
        Assert.Equal(["text/plain"], source!.MimeTypes);
        Assert.Equal("from-the-host", ReadThrough(host, source, "text/plain"));
        Assert.NotNull(output);
    }

    [Fact]
    public void A_guest_selection_reaches_the_host()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Selecting);
        _ = host.CreateOutput();
        var store = new RecordingSelectionStore();
        using var seam = new NestedSeam(host.Backend, store);

        FocusParent(host);
        store.SetSelection(
            SelectionKind.Clipboard,
            new DataSource(["text/plain"], (_, fd) => WriteAndClose(fd, "from-a-guest")),
            SelectionSerial.Unchecked);
        host.Pump();

        var mimeTypes = host.Parent.Invoke(() =>
            host.Parent.Seat!.DataDevice.Selection?.MimeTypes.ToArray() ?? []);
        Assert.Equal(["text/plain"], mimeTypes);
        Assert.Equal("from-a-guest", ReadFromParent(host, "text/plain"));
    }

    [Fact]
    public void A_selection_pushed_inward_is_not_pushed_back_out()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Selecting);
        _ = host.CreateOutput();
        var store = new RecordingSelectionStore();
        using var seam = new NestedSeam(host.Backend, store);

        OfferOnParent(host, "text/plain", "from-the-host");
        host.Pump();
        var bouncedBack = host.Parent.Invoke(() =>
            host.Parent.Seat!.DataDevice.Selection?.Resource is not null);

        Assert.False(bouncedBack);
    }

    [Fact]
    public void A_parent_with_no_data_device_manager_bridges_nothing()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        _ = host.CreateOutput();
        var store = new RecordingSelectionStore();

        using var seam = new NestedSeam(host.Backend, store);
        host.Pump();

        Assert.Empty(store.Changes);
        Assert.Null(store.Current(SelectionKind.Clipboard));
    }

    [Fact]
    public void A_parent_with_no_input_method_leaves_the_bridge_unavailable()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        _ = host.CreateOutput();

        Assert.False(host.Backend.SupportsTextInput);
        using var bridge = new WaylandSeamTextInput(host.Backend);
        var doneCount = 0;
        bridge.Done += () => doneCount++;

        Assert.False(bridge.IsAvailable);
        Assert.False(bridge.HasKeyboardGrab);
        bridge.SurroundingText("abc", 1, 1);
        bridge.ContentType(0, 0);
        bridge.Commit(0);
        bridge.ForwardKey(0, 30, true);
        bridge.ForwardModifiers(0, 0, 0, 0);
        host.Pump();

        Assert.Equal(0, doneCount);
    }

    [Fact]
    public void A_parent_with_no_pointer_constraints_declines_the_lock()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();

        Assert.False(host.Backend.SupportsPointerLock);
        Assert.False(output.LockPointer(true));
        output.SetCursorPositionHint(10, 10);
        Assert.False(output.RequestActivation());
        host.Pump();
    }

    private static void FocusParent(NestedBackendTestHost host)
    {
        host.Pump();
        host.Parent.Invoke(() =>
        {
            host.Parent.Seat!.Keyboard.NotifyEnter(host.Parent.Toplevels[0].Surface);
            host.Parent.Seat!.Keyboard.NotifyKey(10, 30, pressed: true);
        });
        host.Pump();
    }

    private static void OfferOnParent(NestedBackendTestHost host, string mimeType, string payload)
    {
        FocusParent(host);
        host.Parent.Invoke(() => host.Parent.Seat!.DataDevice.SetSelection(
            new DataSource([mimeType], (_, fd) => WriteAndClose(fd, payload))));
        host.Pump();
    }

    private static unsafe string ReadFromParent(NestedBackendTestHost host, string mimeType)
    {
        var fds = stackalloc int[2];
        Assert.Equal(0, pipe(fds));
        var writeEnd = fds[1];
        host.Parent.Invoke(() =>
            host.Parent.Seat!.DataDevice.Selection!.Send(mimeType, new ClientFd(writeEnd, null)));
        return Drain(host, fds[0]);
    }

    private static unsafe string ReadThrough(NestedBackendTestHost host, DataSource source, string mimeType)
    {
        var fds = stackalloc int[2];
        Assert.Equal(0, pipe(fds));
        source.Send(mimeType, new ClientFd(fds[1], null));
        return Drain(host, fds[0]);
    }

    private static unsafe string Drain(NestedBackendTestHost host, int readEnd)
    {
        host.Pump();

        var buffer = new byte[64];
        var total = 0;
        fixed (byte* start = buffer)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var got = read(readEnd, start + total, (nuint)(buffer.Length - total));
                if (got > 0)
                {
                    total += (int)got;
                    break;
                }

                host.Pump(1);
                Thread.Sleep(5);
            }
        }

        close(readEnd);
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static unsafe void WriteAndClose(ClientFd fd, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        fixed (byte* start = bytes)
        {
            _ = write(fd.Value, start, (nuint)bytes.Length);
        }

        fd.Close();
    }

    private sealed class RecordingSelectionStore : ISelectionStore
    {
        private readonly Dictionary<SelectionKind, DataSource?> _sources = [];

        public List<SelectionKind> Changes { get; } = [];

        public event Action<SelectionKind>? SelectionChanged;

        public DataSource? Current(SelectionKind kind) =>
            _sources.TryGetValue(kind, out var source) && source is { IsDestroyed: false } ? source : null;

        public int GetOffer(SelectionKind kind, Span<string> types)
        {
            if (Current(kind) is not { } source)
            {
                return 0;
            }

            if (source.MimeTypes.Count > types.Length)
            {
                return -1;
            }

            for (var i = 0; i < source.MimeTypes.Count; i++)
            {
                types[i] = source.MimeTypes[i];
            }

            return source.MimeTypes.Count;
        }

        public bool SetSelection(SelectionKind kind, DataSource? source, uint serial)
        {
            _sources[kind] = source;
            Changes.Add(kind);
            SelectionChanged?.Invoke(kind);
            return true;
        }

        public bool Receive(SelectionKind kind, string mimeType, ClientFd fd)
        {
            if (Current(kind) is not { } source)
            {
                fd.Close();
                return false;
            }

            source.Send(mimeType, fd);
            return true;
        }
    }
}
