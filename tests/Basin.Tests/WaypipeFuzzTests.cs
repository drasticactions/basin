using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Basin.Diagnostics;
using Basin.Transport.Waypipe;
using Xunit;

namespace Basin.Tests;

public sealed class WaypipeFuzzTests
{
    private const int CasesPerFile = 2000;

    private static readonly WaypipeLimits FuzzLimits = new()
    {
        MaxRegionBytes = 4 * 1024 * 1024,
        MaxTotalRegionBytes = 16 * 1024 * 1024,
        MaxRemoteIds = 64,
        MaxFrameBytes = 8 * 1024 * 1024,
        MaxPipeBytes = 64 * 1024,
    };

    public static TheoryData<string> Corpus => [.. WaypipeCorpus.Names()];

    private static WaypipeChannelOptions OptionsFor(byte[] stream) => new() { CarriesDmabuf = WaypipeCorpus.CarriesDmabuf(stream) };

    private static int SeedOf(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)hash;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void A_corpus_file_replays_to_its_end_without_a_failure(string name)
    {
        BasinCounters.Reset();
        var stream = WaypipeCorpus.Read(name);
        using var host = new WaypipeReplayHost(WaypipeCorpus.CarriesDmabuf(stream));
        var result = host.Replay(
            new WaypipeReplayHost.ReplayStream(stream), WaypipeCorpus.CompressionOf(stream), OptionsFor(stream), new WaypipeLimits());
        if (name.StartsWith("crash-", StringComparison.Ordinal))
        {
            Assert.IsType<WaypipeException>(result.Failure);
            return;
        }

        Assert.Null(result.Failure);
        Assert.True(host.SurfacesCreated > 0, "the replay created no surface, so the corpus drives nothing past the registry");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Mutated_frames_end_the_channel_with_a_named_error_and_leave_nothing_behind(string name)
    {
        TestLogging.WarmStreams();
        BasinCounters.Reset();
        var original = WaypipeCorpus.Read(name);
        var compression = WaypipeCorpus.CompressionOf(original);
        var frames = WaypipeCorpus.FrameOffsets(original);
        var options = OptionsFor(original);
        using var host = new WaypipeReplayHost(options.CarriesDmabuf);
        _ = host.Replay(new WaypipeReplayHost.ReplayStream(original), compression, options, FuzzLimits);
        var liveBaseline = BasinCounters.LiveObjects;
        var fdBaseline = FdSnapshot.Take();
        var seed = SeedOf(name);

        for (var index = 0; index < CasesPerFile; index++)
        {
            var random = new Random(seed + index);
            var mutated = WaypipeCorpus.Mutate(original, frames, random, FuzzLimits, out var what);
            var context = $"{name} case {index} ({what})";
            WaypipeReplayHost.ReplayResult result;
            try
            {
                result = host.Replay(new WaypipeReplayHost.ReplayStream(mutated), compression, options, FuzzLimits);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.IAssertionException)
            {
                throw new Xunit.Sdk.XunitException($"{context}: the host threw {ex}");
            }

            if (result.Failure is not null and not WaypipeException)
            {
                Assert.Fail($"{context}: the channel ended with {result.Failure}");
            }

            Assert.True(result.RegionBytes <= FuzzLimits.MaxTotalRegionBytes, $"{context}: {result.RegionBytes} region bytes held");
            Assert.True(result.LiveRemoteIds <= FuzzLimits.MaxRemoteIds, $"{context}: {result.LiveRemoteIds} remote ids live");
            if (BasinCounters.Enabled && BasinCounters.LiveObjects != liveBaseline)
            {
                Assert.Fail($"{context}: {BasinCounters.LiveObjects - liveBaseline} objects outlived the channel{Environment.NewLine}{BasinCounters.CensusReport()}");
            }

            var leaked = FdSnapshot.Diff(fdBaseline, FdSnapshot.Take());
            if (leaked.Count > 0)
            {
                Assert.Fail($"{context}: fds leaked: " + string.Join(", ", leaked.Select(l => $"{l.Count} x {l.Target}")));
            }
        }
    }

    [Theory]
    [InlineData(WaypipeCompression.Lz4)]
    [InlineData(WaypipeCompression.Zstd)]
    [InlineData(WaypipeCompression.None)]
    public void Record_weston_simple_shm_through_a_live_waypipe(WaypipeCompression compression)
    {
        Assert.SkipUnless(WaypipeCorpus.Recording, "BASIN_RECORD_CORPUS=1 records the corpus");
        var waypipe = Which("waypipe");
        var client = Which("weston-simple-shm");
        Assert.SkipWhen(waypipe is null || client is null, "waypipe and weston-simple-shm must be on PATH");

        var runtime = Path.Combine(Path.GetTempPath(), $"basin-corpus-{Guid.NewGuid():N}"[..36]);
        Directory.CreateDirectory(runtime);
        var socketPath = Path.Combine(runtime, "wp.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        var word = compression.ToString().ToLowerInvariant();
        var info = new ProcessStartInfo(waypipe!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--socket", socketPath, "--no-gpu", "--compress", word, "--display", "wayland-corpus", "server", "--", client! })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = runtime;
        info.Environment.Remove("WAYLAND_DISPLAY");
        using var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var record = new MemoryStream();
        try
        {
            using var accepted = listener.Accept();
            using var host = new WaypipeReplayHost(gpu: false);
            var tee = new WaypipeReplayHost.TeeStream(new NetworkStream(accepted, ownsSocket: false), record);
            using var stop = new Timer(_ => process.Kill(entireProcessTree: true), null, 1500, Timeout.Infinite);
            var result = host.Replay(tee, compression, new WaypipeChannelOptions(), new WaypipeLimits());
            Assert.True(host.SurfacesCreated > 0, "weston-simple-shm created no surface through the channel");
            _ = result;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit();
            Directory.Delete(runtime, recursive: true);
        }

        lock (record)
        {
            WaypipeCorpus.Write($"weston-simple-shm.{word}.bin", record.ToArray(), budget: compression == WaypipeCompression.None ? 320 * 1024 : 96 * 1024);
        }
    }

    [Fact]
    public void Record_the_pipe_and_file_frames()
    {
        Assert.SkipUnless(WaypipeCorpus.Recording, "BASIN_RECORD_CORPUS=1 records the corpus");
        var stream = new List<byte>();
        Span<byte> header = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
        WaypipeWire.WriteConnectionHeader(header, WaypipeWire.ProtocolVersion, WaypipeCompression.None);
        stream.AddRange(header);

        void Frame(WaypipeMessageType type, params int[] words) => FrameWithTail(type, [], words);

        void FrameWithTail(WaypipeMessageType type, ReadOnlySpan<byte> tail, params int[] words)
        {
            var length = 4 + (words.Length * 4) + tail.Length;
            var message = new byte[WaypipeWire.Padded(length)];
            BinaryPrimitives.WriteUInt32LittleEndian(message, WaypipeWire.Header(type, length));
            for (var i = 0; i < words.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(4 + (i * 4)), words[i]);
            }

            tail.CopyTo(message.AsSpan(4 + (words.Length * 4)));
            stream.AddRange(message);
        }

        var pattern = new byte[256];
        for (var i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)(i * 7);
        }

        Frame(WaypipeMessageType.OpenFile, 5, 256);
        FrameWithTail(WaypipeMessageType.BufferFill, pattern, 5, 0, 256);
        var diff = new byte[8 + 4 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(diff, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(4), 5);
        diff[8] = 0xAA;
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 0x01020304);
        FrameWithTail(WaypipeMessageType.BufferDiff, diff, 5, 12, 8);
        Frame(WaypipeMessageType.ExtendFile, 5, 512);
        FrameWithTail(WaypipeMessageType.BufferFill, pattern, 5, 256, 512);
        Frame(WaypipeMessageType.OpenIWPipe, 11);
        FrameWithTail(WaypipeMessageType.PipeTransfer, "basin"u8, 11);
        Frame(WaypipeMessageType.PipeShutdownW, 11);
        Frame(WaypipeMessageType.OpenRWPipe, 12);
        FrameWithTail(WaypipeMessageType.PipeTransfer, "both ways"u8, 12);
        Frame(WaypipeMessageType.PipeShutdownR, 12);
        Frame(WaypipeMessageType.OpenIRPipe, 13);
        Frame(WaypipeMessageType.InjectRIDs, 5);
        Frame(WaypipeMessageType.AckNblocks, 3);
        stream.AddRange(RegistryAndSurface());
        Frame(WaypipeMessageType.Close);
        WaypipeCorpus.Write("pipes-and-files.none.bin", stream.ToArray());
    }

    private static byte[] RegistryAndSurface()
    {
        var wire = new List<byte>();
        wire.AddRange(Protocol(ManagedWire.Request(1, 1, 2u)));
        wire.AddRange(Protocol(ManagedWire.Request(2, 0, 2u, "wl_compositor", 4u, 3u)));
        wire.AddRange(Protocol(ManagedWire.Request(3, 0, 4u)));
        wire.AddRange(Protocol(ManagedWire.Request(4, 6)));
        return [.. wire];

        static byte[] Protocol(byte[] body)
        {
            var length = 4 + body.Length;
            var message = new byte[WaypipeWire.Padded(length)];
            BinaryPrimitives.WriteUInt32LittleEndian(message, WaypipeWire.Header(WaypipeMessageType.Protocol, length));
            body.CopyTo(message.AsSpan(4));
            return message;
        }
    }

    private static string? Which(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':')
            .Select(dir => Path.Combine(dir, program))
            .FirstOrDefault(File.Exists);
}
