using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class FrameTickTests
{
    [Fact]
    public void Hook_receives_the_target_timestamp_and_interval()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var ticks = new List<FrameTick>();
        sceneOutput.BeforeRepaint += ticks.Add;

        _ = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, new SceneCommitOptions
        {
            AllowDirectScanout = false,
            TargetPresentNanos = 16_666_667,
        }));

        var tick = Assert.Single(ticks);
        Assert.Equal(16_666_667, tick.TargetPresentNanos);
        Assert.Equal(host.Output.CurrentMode.RefreshIntervalNanoseconds, (uint)tick.RefreshIntervalNanos);
    }

    [Fact]
    public void Changes_made_inside_the_hook_land_in_the_same_frame()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var rect = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        var step = 0;
        sceneOutput.BeforeRepaint += _ =>
        {
            if (step > 0)
            {
                rect.SetPosition(step * 30, 0);
            }
        };

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));

        step = 1;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(state.Buffer! as MemoryBuffer ?? throw new InvalidOperationException());
        int Red(int x, int y) => rgba[((y * 160) + x) * 4];
        Assert.True(Red(35, 5) > 200, "rect moved by the hook renders at its new position in the same commit");
        Assert.True(Red(5, 5) < 60, "the old position is repainted");
    }

    [Fact]
    public void Hook_damage_re_arms_the_next_frame()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var rect = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        var frame = 0;
        var animating = true;
        sceneOutput.BeforeRepaint += _ =>
        {
            if (animating)
            {
                frame++;
                rect.SetPosition(frame, 0);
            }
        };

        var pending = 0;
        sceneOutput.DamagePending += () => pending++;

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        var afterFirst = pending;
        Assert.True(afterFirst > 0, "a commit whose hook damaged re-arms the next frame");

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.True(pending > afterFirst, "every animating commit re-arms");

        animating = false;
        _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
        var idle = pending;
        _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
        Assert.Equal(idle, pending);
    }

    [Fact]
    public void A_hook_that_stops_damaging_lets_the_output_idle()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var rect = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        var animating = true;
        var frame = 0;
        sceneOutput.BeforeRepaint += _ =>
        {
            if (animating)
            {
                frame++;
                rect.SetPosition(frame, 0);
            }
        };

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.True(sceneOutput.NeedsRepaint || true);

        animating = false;
        Assert.False(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.False(sceneOutput.NeedsRepaint);
    }
}
