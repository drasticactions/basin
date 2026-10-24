using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OracleColorTests
{
    private const int Width = 64;
    private const int Height = 48;

    private static readonly ImageDescription P3 = new()
    {
        PrimariesNamed = ColorPrimaries.DisplayP3,
        TransferNamed = ColorTransferFunction.Gamma22,
    };

    private static readonly ImageDescription Pq = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
    };

    public static TheoryData<string> Renderers => new() { "pixman", "gl" };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void The_oracle_draws_a_tagged_buffer_through_the_same_table_as_the_optimized_path(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");

        var optimized = Render(renderer, fullRepaint: false);
        var oracle = Render(renderer, fullRepaint: true);

        var inside = ((20 * Width) + 20) * 4;
        Assert.True(optimized[inside + 2] > 200 && optimized[inside] < 50, $"the optimized path swapped red to blue: {optimized[inside]},{optimized[inside + 1]},{optimized[inside + 2]}");
        for (var i = 0; i < optimized.Length; i++)
        {
            Assert.True(Math.Abs(optimized[i] - oracle[i]) <= 2, $"byte {i} (pixel {i / 4 % Width},{i / 4 / Width}): optimized {optimized[i]}, oracle {oracle[i]}");
        }
    }

    private static byte[] Render(string rendererName, bool fullRepaint)
    {
        var stack = RendererCatalog.Create(rendererName, CompositorTestHost.RenderNodePath);
        try
        {
            using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
            var scene = new Scene.Scene();
            using var resolver = new SwappingResolver(stack.Renderer);
            var content = SolidBuffer(32, 24, 0xFFFF0000);
            try
            {
                var node = new SceneBuffer(scene.Root) { ColorDescription = Pq };
                node.SetBuffer(content);
                node.SetPosition(8, 8);
                using var driver = new OutputDriver(host, scene, new OutputLayout(), stack.Renderer, stack.DeviceAllocator)
                {
                    HeadlessMode = new OutputMode(Width, Height, 60_000),
                    FullRepaint = fullRepaint,
                };
                driver.CreateInitialOutputs();
                var view = driver.Views[0];
                Assert.NotNull(view.Color);
                view.Color!.Resolver = resolver;
                view.Color.ColorDescription = P3;
                Assert.NotNull(view.Color.LutFor(node));
                var output = (Basin.Backend.Headless.HeadlessOutput)view.Output;
                var start = view.Rendered;
                var deadline = Environment.TickCount64 + 2000;
                while (Environment.TickCount64 < deadline && (view.Rendered < start + 2 || output.PresentedBuffer is null))
                {
                    host.Loop.Dispatch(5);
                }

                Assert.NotNull(output.PresentedBuffer);
                Assert.True(BufferCapture.TryReadRgba(output.PresentedBuffer!, stack.Renderer, out var rgba), "the presented frame reads back");
                node.Destroy();
                return rgba;
            }
            finally
            {
                content.Destroy();
            }
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static MemoryBuffer SolidBuffer(int width, int height, uint pixel)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < width; x++)
                {
                    row[x] = pixel;
                }
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }

    private sealed class SwappingResolver(IRenderer renderer) : IColorTransformResolver, IDisposable
    {
        private IColorLut? _lut;

        public ColorTransformCapability Capability => renderer.ColorTransform;

        public IColorLut? Resolve(ImageDescription source, ImageDescription output)
        {
            if (ImageDescription.ContentComparer.Equals(source, output) || renderer.ColorTransform == ColorTransformCapability.None)
            {
                return null;
            }

            return _lut ??= renderer.ImportLut(SwapRedAndBlue());
        }

        public void Dispose() => _lut?.Dispose();

        private static ColorLut3D SwapRedAndBlue()
        {
            var data = new float[2 * 2 * 2 * 3];
            var index = 0;
            for (var b = 0; b < 2; b++)
            {
                for (var g = 0; g < 2; g++)
                {
                    for (var r = 0; r < 2; r++)
                    {
                        data[index++] = b;
                        data[index++] = g;
                        data[index++] = r;
                    }
                }
            }

            return new ColorLut3D(2, data);
        }
    }
}
