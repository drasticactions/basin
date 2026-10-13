using System.Buffers.Binary;
using Basin;
using Basin.Transport.Waypipe;
using SharpFuzz;
using Wayland.Server;

namespace FuzzWaypipe;

internal static class Program
{
    private static readonly WaypipeLimits Limits = new()
    {
        MaxRegionBytes = 4 * 1024 * 1024,
        MaxTotalRegionBytes = 16 * 1024 * 1024,
        MaxRemoteIds = 64,
        MaxFrameBytes = 8 * 1024 * 1024,
        MaxPipeBytes = 64 * 1024,
    };

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--replay")
        {
            Fuzzer.Run(_ =>
            {
                foreach (var path in args.Skip(1))
                {
                    Console.WriteLine($"{path}: {Replay(File.ReadAllBytes(path)) ?? "ran to its end"}");
                }
            });
            return 0;
        }

        if (args.Length == 3 && args[0] == "--minimize")
        {
            Fuzzer.Run(_ => Minimize(args[1], args[2]));
            return 0;
        }

        Fuzzer.Run(stream =>
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            _ = Replay(copy.ToArray());
        });
        return 0;
    }

    private static void Minimize(string from, string to)
    {
        var input = File.ReadAllBytes(from);
        var signature = Signature(input) ?? throw new InvalidOperationException($"{from} does not crash the harness");
        Console.WriteLine($"{from}: {signature}");
        for (var chunk = Math.Max(1, input.Length / 2); chunk >= 1; chunk /= 2)
        {
            var offset = 0;
            while (offset < input.Length)
            {
                var length = Math.Min(chunk, input.Length - offset);
                byte[] candidate = [.. input.AsSpan(0, offset), .. input.AsSpan(offset + length)];
                if (Signature(candidate) == signature)
                {
                    input = candidate;
                }
                else
                {
                    offset += length;
                }
            }
        }

        File.WriteAllBytes(to, input);
        Console.WriteLine($"{to}: {input.Length} bytes, {signature}");
    }

    private static string? Signature(byte[] input)
    {
        try
        {
            _ = Replay(input);
            return null;
        }
        catch (Exception ex)
        {
            var cause = ex is InvalidOperationException { InnerException: { } inner } ? inner : ex;
            var frame = cause.StackTrace?.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
            return $"{cause.GetType().Name} {frame}";
        }
    }

    private static string? Replay(byte[] input)
    {
        var lead = input.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(input) : 0u;
        var compression = (WaypipeCompression)((lead >> 8) & 0x7);
        if (!Enum.IsDefined(compression))
        {
            compression = WaypipeCompression.Lz4;
        }

        var gpu = input.Length >= 4 && (lead & 0x4) == 0;
        using var host = new Host(gpu);
        var failure = host.Replay(input, compression, new WaypipeChannelOptions { CarriesDmabuf = gpu });
        if (failure is not null and not WaypipeException)
        {
            throw new InvalidOperationException($"the channel ended with {failure.GetType().Name}", failure);
        }

        return failure?.Message;
    }

    private sealed class Host : IDisposable
    {
        private readonly WlServerDisplay _display;
        private readonly WaylandEventLoop _loop;
        private readonly ClientBufferRegistry _buffers = new();
        private readonly ShmGlobal? _shm;
        private readonly CompositorGlobal _compositor;
        private readonly Basin.Seat.Seat? _seat;
        private readonly Basin.Shell.Xdg.XdgShell? _shell;
        private readonly LinuxDmabufGlobal? _dmabuf;

        public Host(bool gpu)
        {
            _display = WlServerDisplay.Create(new ManagedTransport());
            _loop = new WaylandEventLoop(_display);
            if (!gpu)
            {
                _shm = new ShmGlobal(_display, buffers: _buffers);
            }

            _compositor = new CompositorGlobal(_display, _buffers) { Loop = _loop };
            if (gpu)
            {
                _dmabuf = new LinuxDmabufGlobal(
                    _display, _buffers, WaypipeGlobals.ChannelFormats, WaypipeGlobals.SyntheticMainDevice, compositor: _compositor);
            }
            else
            {
                _seat = new Basin.Seat.Seat(_display, _compositor);
                _shell = new Basin.Shell.Xdg.XdgShell(_display, _compositor, _seat);
            }
        }

        public Exception? Replay(byte[] input, WaypipeCompression compression, WaypipeChannelOptions options)
        {
            using var ended = new ManualResetEventSlim(false);
            Exception? failure = null;
            WlClient? client = null;
            var gone = false;
            var channel = WaypipeChannel.AttachChannel(
                new InputStream(input),
                compression,
                Limits,
                options,
                WaypipePump.Async,
                c =>
                {
                    c.Ended += ex =>
                    {
                        failure = ex;
                        ended.Set();
                    };
                    client = _display.CreateClient(c.Transport);
                    client.Destroyed += () => gone = true;
                });

            try
            {
                var deadline = Environment.TickCount64 + 30_000;
                var settled = 0;
                while (settled < 3)
                {
                    _loop.Dispatch(0);
                    _display.FlushClients();
                    if (ended.IsSet)
                    {
                        settled++;
                    }
                    else if (Environment.TickCount64 > deadline)
                    {
                        throw new TimeoutException("the channel never ended");
                    }
                    else
                    {
                        ended.Wait(1);
                    }
                }

                return failure;
            }
            finally
            {
                channel.Dispose();
                _loop.Dispatch(0);
                if (!gone)
                {
                    client?.Destroy();
                }

                _loop.Dispatch(0);
            }
        }

        public void Dispose()
        {
            _loop.Dispatch(0);
            _dmabuf?.Dispose();
            _shell?.Dispose();
            _seat?.Dispose();
            _compositor.Dispose();
            _shm?.Dispose();
            _display.Dispose();
        }
    }

    private sealed class InputStream(byte[] bytes) : Stream
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

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
