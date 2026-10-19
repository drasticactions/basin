using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class BackdropOcclusionTests
{
    public static TheoryData<string, bool> Rows => new()
    {
        { "gl", false },
        { "gl", true },
        { "vulkan", false },
        { "vulkan", true },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void An_opaque_node_above_a_backdrop_does_not_hide_what_the_backdrop_samples(string renderer, bool glass)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };
        IBackdropEffect effect = glass
            ? BackdropGlass.For(host.Renderer)!
            : BackdropBlurs.For(host.Renderer)!;
        using var owned = (IDisposable)effect;
        if (effect is IBackdropBlur blur)
        {
            blur.Options = blur.Options with { Strength = 6, NoiseStrength = 0 };
        }
        else
        {
            ((IBackdropGlass)effect).Options = new GlassOptions { Strength = 6, Noise = 0 };
        }

        var floor = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.12f, 0.16f, 1f));
        var node = new SceneBuffer(host.Scene.Root) { BackdropOnly = true, DestinationWidth = 100, DestinationHeight = 80 };
        node.SetPosition(30, 20);
        using var region = new Pixman.PixmanRegion32(0, 0, 100, 80);
        node.SetBackdropEffect(effect, region);
        var client = new SceneRect(host.Scene.Root, 80, 50, new RenderColor(1f, 0f, 1f, 1f));
        client.SetPosition(40, 40);

        try
        {
            for (var i = 0; i < 4; i++)
            {
                client.Color = (i & 1) == 0 ? new RenderColor(1f, 0f, 1f, 1f) : new RenderColor(0f, 1f, 0f, 1f);
                Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options), $"frame {i}");
                host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
                AssertClose(oracle, state.Buffer!, $"frame {i}");
            }
        }
        finally
        {
            client.Destroy();
            node.Destroy();
            floor.Destroy();
            oracle.Destroy();
        }
    }

    private static void AssertClose(IBuffer expected, IBuffer actual, string what)
    {
        Assert.True(expected.BeginDataAccess(BufferDataAccess.Read, out var e), what);
        Assert.True(actual.BeginDataAccess(BufferDataAccess.Read, out var a), what);
        try
        {
            unsafe
            {
                for (var y = 0; y < expected.Height; y++)
                {
                    var er = new ReadOnlySpan<byte>((void*)(e.Data + (y * e.Stride)), expected.Width * 4);
                    var ar = new ReadOnlySpan<byte>((void*)(a.Data + (y * a.Stride)), expected.Width * 4);
                    for (var x = 0; x < er.Length; x++)
                    {
                        if (Math.Abs(er[x] - ar[x]) > 2)
                        {
                            Assert.Fail($"{what}: pixel ({x / 4},{y}) channel {x % 4} is {ar[x]} against {er[x]} in a full repaint");
                        }
                    }
                }
            }
        }
        finally
        {
            expected.EndDataAccess();
            actual.EndDataAccess();
        }
    }
}
