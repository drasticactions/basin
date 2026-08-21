using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class MeshForeignGlStateTests
{
    [Fact]
    public void A_foreign_attrib_divisor_does_not_break_the_mesh_path()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        var gl = ((Basin.Render.Gl.GlRenderer)host.Renderer).Device.Gl;
        gl.BindVertexArray(0);
        for (var i = 0u; i < 3; i++)
        {
            gl.VertexAttribDivisor(i, 1);
        }

        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.1f, 0.1f, 0.1f, 1f), new Box(0, 0, 128, 96));
        Span<MeshVertex> triangles =
        [
            new(16, 76, 0, 0, new RenderColor(1f, 0f, 0f, 1f)),
            new(60, 12, 0, 0, new RenderColor(1f, 0f, 0f, 1f)),
            new(104, 76, 0, 0, new RenderColor(1f, 0f, 0f, 1f)),
        ];
        pass.AddMesh(null, triangles, new MeshRenderOptions());
        Assert.True(pass.Submit());

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        var i2 = ((44 * target.Width) + 60) * 4;
        Assert.True(rgba[i2] > 200, $"triangle center is {rgba[i2]},{rgba[i2 + 1]},{rgba[i2 + 2]}: the mesh path drew nothing under a foreign attrib divisor");
        target.Destroy();

        for (var i = 0u; i < 3; i++)
        {
            gl.VertexAttribDivisor(i, 0);
        }
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    public void A_wobbly_yank_draws_through_scene_output(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var tree = new SceneTree(host.Scene.Root);
        tree.SetPosition(10, 10);
        _ = new SceneRect(tree, 60, 46, new RenderColor(0.5f, 0.25f, 0.12f, 1f));
        var stack = new TransformStack(tree);
        var wobbly = new Basin.Effects.WobblyEffect();
        wobbly.Attach(stack);
        wobbly.Grab(30, 5);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));

        tree.SetPosition(60, 40);
        wobbly.NotifyMoved(50, 30);
        _ = wobbly.Step(new FrameTick(16_666_667, 16_666_667));
        _ = wobbly.Step(new FrameTick(33_333_334, 16_666_667));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)state.Buffer!);
        var lit = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i] + rgba[i + 1] + rgba[i + 2] > 60)
            {
                lit++;
            }
        }

        wobbly.Release();
        Assert.True(lit > 500, $"the wobbly yank drew only {lit} lit pixels");
    }
}
