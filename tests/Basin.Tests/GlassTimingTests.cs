using Basin.Capabilities;
using Basin.Effects;
using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Renderers;
using Basin.Scene;
using Silk.NET.OpenGLES;
using Silk.NET.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class GlassTimingTests(ITestOutputHelper output)
{
    private const int TargetWidth = 2560;
    private const int TargetHeight = 1440;
    private const int Warmup = 10;
    private const int Frames = 60;

    public static TheoryData<string> Rows => ["gl", "vulkan"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void Gpu_time_per_pass(string row)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("BASIN_GLASS_TIMING") == "1", "set BASIN_GLASS_TIMING=1 to measure");
        CompositorTestHost.SkipUnlessRunnable(row);
        var sizes = new[] { (640, 400), (1280, 800), (2560, 1440) };
        var strengths = new[] { 4, 8, 15 };
        output.WriteLine($"| row | effect | strength | size | median us | p90 us |");
        output.WriteLine("|---|---|---|---|---|---|");
        foreach (var strength in strengths)
        {
            foreach (var (width, height) in sizes)
            {
                foreach (var effect in new[] { "blur", "composite", "fused" })
                {
                    var samples = Measure(row, effect, strength, width, height);
                    Array.Sort(samples);
                    var median = samples[samples.Length / 2] / 1000.0;
                    var p90 = samples[samples.Length * 9 / 10] / 1000.0;
                    output.WriteLine($"| {row} | {effect} | {strength} | {width}x{height} | {median:F0} | {p90:F0} |");
                }
            }
        }
    }

    private static ulong[] Measure(string row, string effect, int strength, int width, int height)
    {
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var floor = new MemoryBuffer(TargetWidth, TargetHeight, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        var target = new MemoryBuffer(TargetWidth, TargetHeight, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32();
        RoundedRegion.Fill(region, width, height, 12);
        IBackdropEffect inner;
        IDisposable owned;
        if (effect == "blur")
        {
            var blur = BackdropBlurs.For(stack.Renderer)!;
            blur.Options = blur.Options with { Strength = strength };
            inner = blur;
            owned = blur;
        }
        else
        {
            var glass = BackdropGlass.For(stack.Renderer)!;
            glass.Options = new GlassOptions { Strength = strength, Pass = effect == "fused" ? GlassPass.Fused : GlassPass.Composite };
            inner = glass;
            owned = glass;
        }

        var timed = row == "gl" ? (TimedEffect)new GlTimedEffect((IGlBackdropEffect)inner) : new VulkanTimedEffect((IVulkanBackdropEffect)inner);
        var key = new object();
        if (inner is IBackdropGlass shaped)
        {
            shaped.SetShape(key, new FrameBackdropShape
            {
                Width = width,
                Height = height,
                CornerRadius = 12,
                EdgeWidth = 10,
                ClientHole = new Box(4, 34, width - 8, height - 38),
                HighlightX = 80,
                HighlightY = 16,
                HighlightStrength = 1,
                Emphasis = 1,
            });
        }

        try
        {
            Fill(floor);
            var floorNode = new SceneBuffer(scene.Root);
            floorNode.SetBuffer(floor);
            var node = new SceneBuffer(scene.Root);
            node.SetPosition((TargetWidth - width) / 2, (TargetHeight - height) / 2);
            node.SetBuffer(veil);
            node.SetBackdropEffect(timed, region, key);
            var samples = new ulong[Frames];
            for (var i = 0; i < Warmup + Frames; i++)
            {
                Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
                var nanos = timed.Collect();
                if (i >= Warmup)
                {
                    samples[i - Warmup] = nanos;
                }
            }

            node.Destroy();
            floorNode.Destroy();
            return samples;
        }
        finally
        {
            timed.Dispose();
            owned.Dispose();
            target.Destroy();
            veil.Destroy();
            floor.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static unsafe void Fill(MemoryBuffer buffer)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var line = (uint*)(view.Data + (y * view.Stride));
            for (var x = 0; x < buffer.Width; x++)
            {
                line[x] = 0xFF000000u | (uint)((x * 7) & 0xFF) << 16 | (uint)((y * 5) & 0xFF) << 8 | (uint)(((x + y) * 3) & 0xFF);
            }
        }

        buffer.EndDataAccess();
    }

    private abstract class TimedEffect : IBackdropEffect, IDisposable
    {
        public abstract ulong Collect();

        public abstract void Dispose();
    }

    private sealed class GlTimedEffect(IGlBackdropEffect inner) : TimedEffect, IGlBackdropEffect
    {
        private const QueryTarget TimeElapsed = (QueryTarget)0x88BF;
        private GL? _gl;
        private uint _query;

        public bool ForgetSurface(object key) => inner.ForgetSurface(key);

        public bool Record(in GlBackdropContext context, out GlBackdropResult result)
        {
            _gl ??= context.Device.Gl;
            if (_query == 0)
            {
                _query = _gl.GenQuery();
            }

            _gl.BeginQuery(TimeElapsed, _query);
            var recorded = inner.Record(context, out result);
            _gl.EndQuery(TimeElapsed);
            return recorded;
        }

        public override ulong Collect()
        {
            if (_gl is null || _query == 0)
            {
                return 0;
            }

            _gl.GetQueryObject(_query, QueryObjectParameterName.Result, out uint nanos);
            return nanos;
        }

        public override void Dispose()
        {
            if (_gl is not null && _query != 0)
            {
                _gl.DeleteQuery(_query);
                _query = 0;
            }
        }
    }

    private sealed unsafe class VulkanTimedEffect(IVulkanBackdropEffect inner) : TimedEffect, IVulkanBackdropEffect
    {
        private VulkanDevice? _device;
        private QueryPool _pool;
        private double _period;
        private bool _written;

        public bool ForgetSurface(object key) => inner.ForgetSurface(key);

        public bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result)
        {
            var device = _device ??= context.Device;
            var vk = device.Api;
            if (_pool.Handle == 0)
            {
                var info = new QueryPoolCreateInfo
                {
                    SType = StructureType.QueryPoolCreateInfo,
                    QueryType = QueryType.Timestamp,
                    QueryCount = 2,
                };
                VulkanDevice.Check(vk.CreateQueryPool(device.Device, in info, null, out _pool), "vkCreateQueryPool");
                vk.GetPhysicalDeviceProperties(device.Physical, out var properties);
                _period = properties.Limits.TimestampPeriod;
            }

            vk.CmdResetQueryPool(context.Commands, _pool, 0, 2);
            vk.CmdWriteTimestamp(context.Commands, PipelineStageFlags.TopOfPipeBit, _pool, 0);
            var recorded = inner.Record(context, out result);
            vk.CmdWriteTimestamp(context.Commands, PipelineStageFlags.BottomOfPipeBit, _pool, 1);
            _written = true;
            return recorded;
        }

        public override ulong Collect()
        {
            if (_device is null || !_written)
            {
                return 0;
            }

            var stamps = stackalloc ulong[2];
            var status = _device.Api.GetQueryPoolResults(
                _device.Device, _pool, 0, 2, (nuint)(2 * sizeof(ulong)), stamps, sizeof(ulong),
                QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit);
            _written = false;
            return status == Result.Success ? (ulong)((stamps[1] - stamps[0]) * _period) : 0;
        }

        public override void Dispose()
        {
            if (_device is not null && _pool.Handle != 0)
            {
                _ = _device.Api.DeviceWaitIdle(_device.Device);
                _device.Api.DestroyQueryPool(_device.Device, _pool, null);
                _pool = default;
            }
        }
    }
}
