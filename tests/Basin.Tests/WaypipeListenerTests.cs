using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Basin.Diagnostics;
using Basin.Transport.Waypipe;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class WaypipeListenerTests
{
    public static TheoryData<string> Kinds => ["unix", "tcp", "vsock"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Two_peers_become_two_clients_on_the_loop_and_one_ending_leaves_the_other(string kind)
    {
        using var rig = new Rig(kind);
        var threads = new List<int>();
        rig.Listener.ChannelAccepted += channel =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            rig.Adoption.Adopt(channel);
        };

        using var first = rig.Connect();
        using var second = rig.Connect();
        rig.PumpUntil(() => threads.Count == 2);

        Assert.All(threads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
        Assert.Equal(2, rig.Adoption.Count);
        Assert.Equal(2, rig.Listener.Live);
        Assert.Equal(2, rig.Listener.Accepted);

        first.SendConnectionHeader();
        second.SendConnectionHeader();
        first.Dispose();
        rig.PumpUntil(() => rig.Listener.Live == 1 && rig.Adoption.Count == 1);

        second.SendGetRegistry();
        rig.PumpUntil(second.ReceivedProtocol);
        Assert.Equal(2, rig.Listener.Accepted);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Dispose_closes_every_channel_and_leaves_nothing_behind(string kind)
    {
        string? path;
        var baseline = Baseline();
        using (var rig = new Rig(kind))
        {
            path = rig.UnixPath;
            rig.Listener.ChannelAccepted += channel => rig.Adoption.Adopt(channel);
            using var first = rig.Connect();
            using var second = rig.Connect();
            rig.PumpUntil(() => rig.Listener.Live == 2);
            first.SendConnectionHeader();
            second.SendConnectionHeader();
            rig.PumpUntil(() => first.Received(WaypipeMessageType.Version) && second.Received(WaypipeMessageType.Version));

            rig.Listener.Dispose();
            Assert.Equal(0, rig.Listener.Live);
            Assert.True(first.ReadsToEnd(), "the first peer never saw its channel close");
            Assert.True(second.ReadsToEnd(), "the second peer never saw its channel close");
            rig.PumpUntil(() => rig.Adoption.Count == 0);
            if (path is not null)
            {
                Assert.False(File.Exists(path), "the listener left its socket file behind");
            }
        }

        LeakTracking.Expect(0, BasinCounters.LiveObjects);
        LeakTracking.Expect(0, BasinCounters.PendingFrees);
        var leaked = FdSnapshot.Diff(baseline, FdSnapshot.Take());
        Assert.True(leaked.Count == 0, "fds leaked: " + string.Join(", ", leaked.Select(l => $"{l.Count} x {l.Target}")));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_listener_starts_no_thread_and_each_channel_starts_one_reader(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "thread names are read from /proc");
        var before = Threads();
        using var rig = new Rig(kind);
        Assert.Empty(Started(before));

        rig.Listener.ChannelAccepted += channel => rig.Adoption.Adopt(channel);
        using var first = rig.Connect();
        using var second = rig.Connect();
        rig.PumpUntil(() => rig.Listener.Live == 2);
        var started = Started(before);
        Assert.Equal(2, started.Count);
        Assert.All(started, name => Assert.Equal("basin-waypipe-r", name));
    }

    [Fact]
    public void An_ip_wildcard_is_refused_at_bind()
    {
        using var display = WlServerDisplay.Create(new ManagedTransport());
        var loop = new WaylandEventLoop(display);
        Assert.Throws<ArgumentException>(() => WaypipeListener.Bind(loop, new IPEndPoint(IPAddress.Any, 0)));
        Assert.Throws<ArgumentException>(() => WaypipeListener.Bind(loop, new IPEndPoint(IPAddress.IPv6Any, 0)));
    }

    private static Dictionary<string, int> Baseline()
    {
        BasinCounters.Reset();
        TestLogging.WarmStreams();
        TestLogging.WarmSockets();
        return FdSnapshot.Take();
    }

    private static Dictionary<string, string> Threads()
    {
        var threads = new Dictionary<string, string>();
        foreach (var task in Directory.GetDirectories("/proc/self/task"))
        {
            try
            {
                threads[Path.GetFileName(task)] = File.ReadAllText(Path.Combine(task, "comm")).Trim();
            }
            catch (IOException)
            {
            }
        }

        return threads;
    }

    private static List<string> Started(Dictionary<string, string> before) =>
        [.. Threads()
            .Where(thread => !before.ContainsKey(thread.Key) && !thread.Value.StartsWith(".NET", StringComparison.Ordinal))
            .Select(thread => thread.Value)];

    private sealed class Rig : IDisposable
    {
        private readonly WlServerDisplay _display;
        private readonly ClientBufferRegistry _buffers = new();
        private readonly CompositorGlobal _compositor;
        private readonly string _kind;

        public Rig(string kind)
        {
            _kind = kind;
            if (kind == "vsock")
            {
                Assert.SkipWhen(VsockFacts.LoopbackReason() is not null, VsockFacts.LoopbackReason() ?? string.Empty);
            }

            _display = WlServerDisplay.Create(new ManagedTransport());
            Loop = new WaylandEventLoop(_display);
            _compositor = new CompositorGlobal(_display, _buffers);
            Adoption = new WaypipeAdoption(_display, _buffers, _compositor, hostHasDmabuf: false);
            EndPoint endpoint = kind switch
            {
                "unix" => new UnixDomainSocketEndPoint(UnixPath = Path.Combine(Path.GetTempPath(), $"basin-wp-{Guid.NewGuid():N}"[..40])),
                "tcp" => new IPEndPoint(IPAddress.Loopback, 0),
                _ => new VsockEndPoint(VsockEndPoint.AnyCid, VsockEndPoint.AnyPort),
            };
            Listener = WaypipeListener.Bind(Loop, endpoint, WaypipeCompression.None);
        }

        public WaylandEventLoop Loop { get; }

        public WaypipeListener Listener { get; }

        public WaypipeAdoption Adoption { get; }

        public string? UnixPath { get; }

        public Peer Connect()
        {
            Socket socket;
            if (_kind == "vsock" && OperatingSystem.IsLinux())
            {
                var port = ((VsockEndPoint)Listener.LocalEndPoint).Port;
                socket = new Socket(new SafeSocketHandle(VsockNative.Connect(new VsockEndPoint(VsockEndPoint.LocalCid, port)), ownsHandle: true));
            }
            else
            {
                socket = new Socket(
                    Listener.LocalEndPoint.AddressFamily,
                    SocketType.Stream,
                    _kind == "unix" ? ProtocolType.Unspecified : ProtocolType.Tcp);
                socket.Connect(Listener.LocalEndPoint);
            }

            return new Peer(socket);
        }

        public void PumpUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 10_000;
            while (!condition())
            {
                Assert.True(Environment.TickCount64 < deadline, "the condition was never reached while pumping");
                Loop.Dispatch(10);
                _display.FlushClients();
            }
        }

        public void Dispose()
        {
            Adoption.Dispose();
            Listener.Dispose();
            Loop.Dispatch(0);
            _compositor.Dispose();
            _display.Dispose();
        }
    }

    private sealed class Peer(Socket socket) : IDisposable
    {
        private readonly List<byte> _received = [];
        private bool _disposed;

        public void SendConnectionHeader()
        {
            Span<byte> header = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
            WaypipeWire.WriteConnectionHeader(header, WaypipeWire.ProtocolVersion, WaypipeCompression.None);
            socket.Send(header);
        }

        public void SendGetRegistry()
        {
            Span<byte> frame = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, WaypipeWire.Header(WaypipeMessageType.Protocol, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(frame[4..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(frame[8..], (12u << 16) | 1);
            BinaryPrimitives.WriteUInt32LittleEndian(frame[12..], 2);
            socket.Send(frame);
        }

        public bool ReceivedProtocol() => Received(WaypipeMessageType.Protocol);

        public bool Received(WaypipeMessageType wanted)
        {
            Drain();
            var bytes = _received.ToArray();
            var offset = 0;
            while (offset + 4 <= bytes.Length)
            {
                var (length, type) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
                if (length < 4 || offset + length > bytes.Length)
                {
                    break;
                }

                if (type == wanted && length > 4)
                {
                    return true;
                }

                offset += WaypipeWire.Padded(length);
            }

            return false;
        }

        public bool ReadsToEnd()
        {
            socket.ReceiveTimeout = 10_000;
            var chunk = new byte[4096];
            try
            {
                while (socket.Receive(chunk) > 0)
                {
                }

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                socket.Dispose();
            }
        }

        private void Drain()
        {
            var chunk = new byte[4096];
            while (socket.Available > 0)
            {
                var got = socket.Receive(chunk);
                if (got <= 0)
                {
                    break;
                }

                _received.AddRange(chunk.AsSpan(0, got));
            }
        }
    }
}
