using Basin.Diagnostics;
using Basin.Transport.Waypipe;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

internal sealed class WaypipeReplayHost : IDisposable
{
    public WaypipeReplayHost(bool gpu)
    {
        Display = WlServerDisplay.Create(new ManagedTransport());
        Loop = new WaylandEventLoop(Display);
        Buffers = new ClientBufferRegistry();
        if (!gpu)
        {
            Shm = new ShmGlobal(Display, buffers: Buffers);
        }

        Compositor = new CompositorGlobal(Display, Buffers) { Loop = Loop };
        Compositor.SurfaceCreated += _ => SurfacesCreated++;
        if (!gpu)
        {
            Seat = new Basin.Seat.Seat(Display, Compositor);
            Shell = new Basin.Shell.Xdg.XdgShell(Display, Compositor, Seat);
        }
        else
        {
            Dmabuf = new LinuxDmabufGlobal(
                Display,
                Buffers,
                WaypipeGlobals.ChannelFormats,
                WaypipeGlobals.SyntheticMainDevice,
                compositor: Compositor);
        }
    }

    public WlServerDisplay Display { get; }

    public WaylandEventLoop Loop { get; }

    public ClientBufferRegistry Buffers { get; }

    public ShmGlobal? Shm { get; }

    public CompositorGlobal Compositor { get; }

    public Basin.Seat.Seat? Seat { get; }

    public Basin.Shell.Xdg.XdgShell? Shell { get; }

    public LinuxDmabufGlobal? Dmabuf { get; }

    public int SurfacesCreated { get; private set; }

    public ReplayResult Replay(Stream stream, WaypipeCompression compression, WaypipeChannelOptions options, WaypipeLimits limits, int timeoutMillis = 10_000)
    {
        using var ended = new ManualResetEventSlim(false);
        Exception? failure = null;
        WlClient? client = null;
        var clientGone = false;
        var channel = WaypipeChannel.AttachChannel(
            stream,
            compression,
            limits,
            options,
            WaypipePump.Thread,
            c =>
            {
                c.Ended += ex =>
                {
                    failure = ex;
                    ended.Set();
                };
                client = Display.CreateClient(c.Transport);
                client.Destroyed += () => clientGone = true;
            });

        try
        {
            var deadline = Environment.TickCount64 + timeoutMillis;
            var settled = 0;
            while (settled < 3)
            {
                Loop.Dispatch(0);
                Display.FlushClients();
                if (ended.IsSet)
                {
                    settled++;
                }
                else if (Environment.TickCount64 > deadline)
                {
                    Assert.Fail("the channel never ended inside the timeout");
                }
                else
                {
                    ended.Wait(1);
                }
            }

            var result = new ReplayResult(failure, channel.Engine.RegionBytes, channel.Engine.LiveRemoteIds);
            return result;
        }
        finally
        {
            channel.Dispose();
            Loop.Dispatch(0);
            if (!clientGone)
            {
                client?.Destroy();
            }

            Loop.Dispatch(0);
            Loop.Dispatch(0);
        }
    }

    public void Dispose()
    {
        Loop.Dispatch(0);
        Dmabuf?.Dispose();
        Shell?.Dispose();
        Seat?.Dispose();
        Compositor.Dispose();
        Shm?.Dispose();
        Display.Dispose();
        Assert.Equal(0, Buffers.Count);
        if (BasinCounters.Enabled)
        {
            Assert.True(BasinCounters.PendingFrees == 0, $"{BasinCounters.PendingFrees} deferred frees pending at teardown");
        }
    }

    internal readonly record struct ReplayResult(Exception? Failure, long RegionBytes, int LiveRemoteIds);

    internal sealed class ReplayStream(byte[] bytes, Stream? writes = null) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var take = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsSpan(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => writes?.Write(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class TeeStream(Stream inner, MemoryStream record) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var got = inner.Read(buffer);
            lock (record)
            {
                record.Write(buffer[..got]);
            }

            return got;
        }

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
