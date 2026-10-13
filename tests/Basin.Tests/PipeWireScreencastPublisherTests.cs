using Basin.Capabilities;
using Basin.Scene;
using Basin.Screencast.PipeWire;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;
using Xunit;

namespace Basin.Tests;

public sealed class PipeWireScreencastPublisherTests
{
    private static void SkipWithoutPipeWire()
    {
        Assert.SkipUnless(PipeWireLibraryProbe.IsAvailable(out var whyNot), whyNot ?? "libpipewire");
        Assert.SkipUnless(PipeWireLibraryProbe.IsDaemonReachable(), "no PipeWire daemon: PIPEWIRE_REMOTE unset and $XDG_RUNTIME_DIR/pipewire-0 absent");
    }

    private static void SkipWithoutSessionManager()
    {
        SkipWithoutPipeWire();
        _sessionManager ??= HasSessionManager();
        if (!_sessionManager.Value)
        {
            const string reason = "no PipeWire session manager: the daemon has no \"default\" metadata, so nothing links a consumer to a node. Start one with `systemctl --user start wireplumber`";
            TestContext.Current.AddWarning(reason);
            Assert.Skip(reason);
        }
    }

    private static bool? _sessionManager;

    private static bool HasSessionManager()
    {
        PipeWireLibrary.Init();
        using var loop = PipeWireLoop.Create();
        using var context = new PipeWireContext(loop);
        using var core = context.Connect();
        var registry = core.GetRegistry();
        var found = false;
        var done = false;
        registry.GlobalAdded += (_, global) =>
        {
            if (global.Type.StartsWith("PipeWire:Interface:Metadata", StringComparison.Ordinal)
                && global.GetProperty("metadata.name") == "default")
            {
                found = true;
            }
        };
        var sequence = core.Sync(0, 0);
        core.Done += (_, e) => done |= e.Id == 0 && e.Sequence == sequence;
        var deadline = Environment.TickCount64 + 2000;
        while (!done && !found && Environment.TickCount64 < deadline)
        {
            loop.Enter();
            try
            {
                loop.Iterate(10);
            }
            finally
            {
                loop.Leave();
            }
        }

        return found;
    }

    private sealed class Consumer : IDisposable
    {
        private readonly PipeWireLoop _loop;
        private readonly PipeWireContext _context;
        private readonly PipeWireCore _core;
        private readonly PipeWireStream _stream;

        public Consumer(uint nodeId, int width, int height, ulong[]? dmabufModifiers = null, bool nv12 = false)
        {
            PipeWireLibrary.Init();
            _loop = PipeWireLoop.Create();
            _context = new PipeWireContext(_loop);
            _core = _context.Connect();
            using var properties = PipeWireProperties.From(
                "media.type", "Video",
                "media.category", "Capture",
                "media.role", "Screen",
                "node.target", nodeId.ToString());
            _stream = new PipeWireStream(_core, "basin-test-consumer", properties);
            _stream.Process = s =>
            {
                var buffer = s.DequeueBuffer();
                if (buffer.IsNull)
                {
                    return;
                }

                var data = buffer[0];
                DataType = data.DataType;
                LastFd = data.Fd;
                DataCount = buffer.DataCount;
                if (buffer.DataCount >= 2)
                {
                    var chroma = buffer[1];
                    Planes = ((int)data.Fd, data.Offset, data.Stride, (int)chroma.Fd, chroma.Offset, chroma.Stride);
                }
                if (data.HasMemory && data.Size >= 4)
                {
                    var memory = data.Memory;
                    FirstPixel = (uint)(memory[0] | (memory[1] << 8) | (memory[2] << 16) | (memory[3] << 24));
                }

                if (buffer.TryGetHeader(out var header))
                {
                    LastSequence = header.Seq;
                }

                if (buffer.TryGetVideoDamage(out var damage) && damage.Count > 0)
                {
                    LastDamage = new Box(damage[0].X, damage[0].Y, (int)damage[0].Width, (int)damage[0].Height);
                }

                if (buffer.TryGetCursor(out var cursor))
                {
                    CursorSeen |= cursor.IsValid;
                    if (cursor.HasBitmap)
                    {
                        CursorBitmaps++;
                    }
                }

                Frames++;
                s.QueueBuffer(buffer);
            };
            _stream.ParamChanged += (sender, e) =>
            {
                if (e.ParamType == spa_param_type.SPA_PARAM_Format && !e.Param.IsNull)
                {
                    ((PipeWireStream)sender!).UpdateParams(
                        SpaMetaParams.BuildHeader(),
                        SpaMetaParams.BuildVideoDamage(),
                        SpaMetaParams.BuildCursor());
                }
            };
            _stream.StateChanged += (_, e) =>
            {
                States.Add(e.NewState);
                if (e.NewState == pw_stream_state.PW_STREAM_STATE_STREAMING)
                {
                    Streaming = true;
                }
                else if (e.NewState == pw_stream_state.PW_STREAM_STATE_ERROR)
                {
                    Error = e.Error ?? "error";
                }
            };
            var flags = pw_stream_flags.PW_STREAM_FLAG_AUTOCONNECT;
            byte[][] formats;
            if (nv12)
            {
                formats =
                [
                    SpaVideoFormats.BuildDmaBuf(
                        [spa_video_format.SPA_VIDEO_FORMAT_NV12], [DrmFormatSet.ModifierLinear],
                        ((uint)width, (uint)height), (1, 1), (8192, 8192), dontFixate: false),
                ];
            }
            else if (dmabufModifiers is { Length: > 0 })
            {
                formats = SpaVideoFormats.BuildDmaBufAndFallback(
                    [spa_video_format.SPA_VIDEO_FORMAT_BGRx, spa_video_format.SPA_VIDEO_FORMAT_BGRA],
                    dmabufModifiers,
                    ((uint)width, (uint)height),
                    (1, 1),
                    (8192, 8192));
            }
            else
            {
                flags |= pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS;
                formats =
                [
                    SpaVideoFormats.BuildRawChoice(
                        [spa_video_format.SPA_VIDEO_FORMAT_BGRx, spa_video_format.SPA_VIDEO_FORMAT_BGRA],
                        ((uint)width, (uint)height),
                        (1, 1),
                        (8192, 8192)),
                ];
            }

            _stream.Connect(spa_direction.SPA_DIRECTION_INPUT, nodeId, flags, formats);
        }

        public int Frames { get; private set; }

        public int DataCount { get; private set; }

        public (int LumaFd, uint LumaOffset, int LumaStride, int ChromaFd, uint ChromaOffset, int ChromaStride) Planes { get; private set; }

        public List<pw_stream_state> States { get; } = [];

        public bool Streaming { get; private set; }

        public string? Error { get; private set; }

        public spa_data_type DataType { get; private set; }

        public long LastFd { get; private set; } = -1;

        public uint FirstPixel { get; private set; }

        public ulong LastSequence { get; private set; }

        public Box LastDamage { get; private set; }

        public bool CursorSeen { get; private set; }

        public int CursorBitmaps { get; private set; }

        public void Pump()
        {
            _loop.Enter();
            try
            {
                _loop.Iterate(10);
            }
            finally
            {
                _loop.Leave();
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
            _core.Dispose();
            _context.Dispose();
            _loop.Dispose();
        }
    }

    private static void PumpUntil(CompositorTestHost host, Consumer consumer, Func<bool> condition, int rounds = 300)
    {
        for (var i = 0; i < rounds && !condition(); i++)
        {
            host.Loop.Dispatch(5);
            consumer.Pump();
            Assert.Null(consumer.Error);
        }

        Assert.True(condition(), $"condition not reached while pumping the two loops; consumer saw {consumer.Frames} frames, states {string.Join(",", consumer.States)}");
    }

    [Fact]
    public void A_published_output_delivers_frames_with_header_and_damage()
    {
        SkipWithoutSessionManager();
        using var host = new CompositorTestHost(64, 48);
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(1f, 0f, 0f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        using var publisher = PipeWireScreencastPublisher.TryCreate(host.Loop, capture, host.Layout, clientName: "basin-test");
        Assert.NotNull(publisher);

        var request = new ScreencastRequest { StreamId = 7, Source = CaptureSource.Output(host.Output), Cursor = ScreencastCursorMode.Metadata };
        Assert.True(publisher.TryPublish(request, out var info), info.FailureReason);
        Assert.NotEqual(0u, info.NodeId);
        Assert.False(info.DmabufOffered);
        Assert.Equal(1, publisher.StreamCount);

        using var consumer = new Consumer(info.NodeId, 64, 48);
        PumpUntil(host, consumer, () => consumer.Streaming);
        PumpUntil(host, consumer, () => consumer.Frames >= 1);

        for (var i = 0; i < 2; i++)
        {
            var seen = consumer.Frames;
            capture.NotifyDamaged(host.Output, new Box(4, 4, 8, 8));
            PumpUntil(host, consumer, () => consumer.Frames > seen);
        }

        Assert.True(consumer.Frames >= 3, $"only {consumer.Frames} frames arrived");
        Assert.Equal(spa_data_type.SPA_DATA_MemFd, consumer.DataType);
        Assert.Equal(0x00FF0000u, consumer.FirstPixel & 0x00FFFFFFu);
        Assert.Equal((ulong)(consumer.Frames - 1), consumer.LastSequence);
        Assert.Equal(new Box(4, 4, 8, 8), consumer.LastDamage);
        Assert.True(publisher.TryGetNegotiated(7, out var dmabuf, out var nodeId));
        Assert.False(dmabuf);
        Assert.Equal(info.NodeId, nodeId);

        publisher.Close(7);
        Assert.Equal(0, publisher.StreamCount);
        Assert.False(publisher.TryGetNegotiated(7, out _, out _));
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int munmap(nint address, nuint length);

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public unsafe void A_consumer_that_asks_for_nv12_gets_bt709_limited_planes(string renderer)
    {
        SkipWithoutSessionManager();
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 48, renderer);
        Assert.SkipWhen(host.DeviceAllocator is null, "no device allocator");
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(1f, 0f, 0f, 1f));
        using var capture = new SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Allocator = host.DeviceAllocator,
            Background = RenderColor.Black,
        };
        Assert.True(capture.CanCaptureYuv);
        using var publisher = PipeWireScreencastPublisher.TryCreate(host.Loop, capture, host.Layout, host.DeviceAllocator, "basin-test");
        Assert.NotNull(publisher);
        var request = new ScreencastRequest { StreamId = 9, Source = CaptureSource.Output(host.Output) };
        Assert.True(publisher.TryPublish(request, out var info), info.FailureReason);

        using var consumer = new Consumer(info.NodeId, 64, 48, nv12: true);
        PumpUntil(host, consumer, () => consumer.Streaming);
        PumpUntil(host, consumer, () => consumer.Frames >= 1);
        Assert.Equal(spa_data_type.SPA_DATA_DmaBuf, consumer.DataType);
        Assert.Equal(2, consumer.DataCount);

        var planes = consumer.Planes;
        var length = (nuint)(planes.ChromaOffset + (planes.ChromaStride * 24));
        var map = mmap(0, length, 1, 1, planes.LumaFd, 0);
        Assert.NotEqual(-1, (long)map);
        try
        {
            var expected = YuvConversion.Reference(255, 0, 0, YuvCoefficients.Bt709, YuvRange.Limited);
            var luma = ((byte*)map)[planes.LumaOffset + (uint)(10 * planes.LumaStride) + 10];
            var chroma = (byte*)map + planes.ChromaOffset + (5 * planes.ChromaStride) + 10;
            Assert.InRange(luma, expected.Y - 1, expected.Y + 1);
            Assert.InRange(chroma[0], expected.Cb - 1, expected.Cb + 1);
            Assert.InRange(chroma[1], expected.Cr - 1, expected.Cr + 1);
        }
        finally
        {
            _ = munmap(map, length);
        }

        publisher.Close(9);
    }

    [Fact]
    public void A_frame_whose_capture_failed_is_retried_without_new_damage()
    {
        SkipWithoutSessionManager();
        using var host = new CompositorTestHost(64, 48);
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(0f, 0f, 1f, 1f));
        var capture = new FailingCapture(new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black }, 2);
        using var publisher = PipeWireScreencastPublisher.TryCreate(host.Loop, capture, host.Layout, clientName: "basin-test");
        Assert.NotNull(publisher);
        var request = new ScreencastRequest { StreamId = 11, Source = CaptureSource.Output(host.Output) };
        Assert.True(publisher.TryPublish(request, out var info), info.FailureReason);

        using var consumer = new Consumer(info.NodeId, 64, 48);
        PumpUntil(host, consumer, () => consumer.Streaming);
        PumpUntil(host, consumer, () => consumer.FirstPixel != 0);
        Assert.Equal(0, capture.Remaining);
        Assert.Equal(0x000000FFu, consumer.FirstPixel & 0x00FFFFFFu);
        publisher.Close(11);
    }

    private sealed class FailingCapture(IScreenCapture inner, int failures) : IScreenCapture
    {
        public int Remaining { get; private set; } = failures;

        public bool Supports(in CaptureSource source) => inner.Supports(source);

        public bool TryDescribe(in CaptureSource source, out CaptureFormat format) => inner.TryDescribe(source, out format);

        public bool Capture(in CaptureSource source, in Box region, IBuffer target)
        {
            if (Remaining > 0)
            {
                Remaining--;
                return false;
            }

            return inner.Capture(source, region, target);
        }

        public bool TryCursorState(IOutput output, out CaptureCursorState cursor) => inner.TryCursorState(output, out cursor);

        public void SetCursor(IBuffer? image, in CaptureCursorState state) => inner.SetCursor(image, state);

        public void AddDamageObserver(ICaptureDamageObserver observer) => inner.AddDamageObserver(observer);

        public void RemoveDamageObserver(ICaptureDamageObserver observer) => inner.RemoveDamageObserver(observer);
    }

    [Fact]
    public void A_source_that_cannot_be_captured_is_refused_by_name()
    {
        SkipWithoutPipeWire();
        using var host = new CompositorTestHost();
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        using var publisher = PipeWireScreencastPublisher.TryCreate(host.Loop, capture, host.Layout, clientName: "basin-test");
        Assert.NotNull(publisher);

        var request = new ScreencastRequest { StreamId = 1, Source = CaptureSource.Toplevel(99) };
        Assert.False(publisher.TryPublish(request, out var info));
        Assert.Equal(0u, info.NodeId);
        Assert.NotNull(info.FailureReason);
        Assert.Equal(0, publisher.StreamCount);
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void A_gpu_row_offers_dmabuf(string renderer)
    {
        SkipWithoutSessionManager();
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 48, renderer);
        var allocator = host.DeviceAllocator;
        Assert.SkipWhen(allocator is null, $"{renderer} has no device allocator");
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(0f, 1f, 0f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        using var publisher = PipeWireScreencastPublisher.TryCreate(host.Loop, capture, host.Layout, allocator, "basin-test");
        Assert.NotNull(publisher);
        {
            var request = new ScreencastRequest { StreamId = 3, Source = CaptureSource.Output(host.Output) };
            Assert.True(publisher.TryPublish(request, out var info), info.FailureReason);
            Assert.True(info.DmabufOffered);

            var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888)
                .Where(m => m != DrmFormatSet.ModifierInvalid).ToArray();
            using (var plain = new Consumer(info.NodeId, 64, 48))
            {
                PumpUntil(host, plain, () => plain.Streaming);
                PumpUntil(host, plain, () => plain.Frames >= 1);
                Assert.True(publisher.TryGetNegotiated(3, out var dmabuf, out _));
                Assert.False(dmabuf);
                Assert.Equal(spa_data_type.SPA_DATA_MemFd, plain.DataType);
                Assert.Equal(0x0000FF00u, plain.FirstPixel & 0x00FFFFFFu);
            }

            publisher.Close(3);
            Assert.True(publisher.TryPublish(request with { StreamId = 4 }, out info), info.FailureReason);
            using (var gpu = new Consumer(info.NodeId, 64, 48, modifiers))
            {
                PumpUntil(host, gpu, () => gpu.Streaming);
                PumpUntil(host, gpu, () => gpu.Frames >= 1);
                capture.NotifyDamaged(host.Output, new Box(0, 0, 64, 48));
                PumpUntil(host, gpu, () => gpu.Frames >= 2);
                Assert.True(publisher.TryGetNegotiated(4, out var dmabuf, out _));
                Assert.True(dmabuf, "the GPU consumer did not negotiate DmaBuf");
                Assert.Equal(spa_data_type.SPA_DATA_DmaBuf, gpu.DataType);
                Assert.True(gpu.LastFd >= 0);
            }

            publisher.Close(4);
        }
    }
}
