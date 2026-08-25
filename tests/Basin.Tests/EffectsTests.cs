using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class EffectTimelineTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void Progress_follows_the_target_timestamps()
    {
        var timeline = new EffectTimeline { Easing = EasingCurve.Linear };
        timeline.Start(Tick(0), 100_000_000);
        Assert.Equal(0, timeline.Progress(Tick(0)), 6);
        Assert.Equal(0.5, timeline.Progress(Tick(50)), 6);
        Assert.Equal(1, timeline.Progress(Tick(100)), 6);
        Assert.Equal(1, timeline.Progress(Tick(150)), 6);
    }

    [Fact]
    public void Running_reports_one_extra_tick_after_finishing()
    {
        var timeline = new EffectTimeline { Easing = EasingCurve.Linear };
        timeline.Start(Tick(0), 100_000_000);
        Assert.True(timeline.Running(Tick(50)));
        Assert.True(timeline.Running(Tick(120)));
        Assert.False(timeline.Running(Tick(140)));
    }

    [Fact]
    public void Restart_preserves_visual_progress()
    {
        var timeline = new EffectTimeline { Easing = EasingCurve.Linear };
        timeline.Start(Tick(0), 100_000_000);
        Assert.Equal(0.7, timeline.Progress(Tick(70)), 6);
        timeline.RestartPreservingProgress(Tick(70));
        Assert.Equal(0.3, timeline.Progress(Tick(70)), 3);
        Assert.Equal(1, timeline.Progress(Tick(140)), 6);
    }

    [Fact]
    public void Easings_hit_the_endpoints()
    {
        foreach (var easing in new[] { EasingCurve.Linear, EasingCurve.Sigmoid, EasingCurve.Circle, EasingCurve.CubicBezier(0.25, 0.1, 0.25, 1) })
        {
            Assert.Equal(0, easing.Apply(0), 4);
            Assert.Equal(1, easing.Apply(1), 4);
            var mid = easing.Apply(0.5);
            Assert.InRange(mid, 0.0, 1.0);
        }
    }
}

public sealed class WobblyEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack, WobblyEffect Effect, SceneRect Content) Attach()
    {
        var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(40, 30);
        var content = new SceneRect(window, 60, 40, new RenderColor(0.8f, 0.4f, 0.2f, 1f));
        var stack = new TransformStack(window);
        var effect = new WobblyEffect();
        effect.Attach(stack);
        return (host, stack, effect, content);
    }

    [Fact]
    public void A_released_wobble_settles_and_detaches_the_deformer()
    {
        var (host, _, effect, _) = Attach();
        using var guard = host;

        effect.Grab(30, 20);
        effect.NotifyMoved(25, 0);
        effect.Release();
        Assert.True(effect.IsWobbling);

        var settled = false;
        for (var frame = 1; frame < 600; frame++)
        {
            if (!effect.Step(Tick(frame * 16)))
            {
                settled = true;
                break;
            }
        }

        Assert.True(settled, "the spring model settles below the velocity and force thresholds");
        Assert.False(effect.IsWobbling);
    }

    [Fact]
    public void A_grabbed_wobble_never_settles()
    {
        var (host, _, effect, _) = Attach();
        using var guard = host;

        effect.Grab(30, 20);
        effect.NotifyMoved(25, 10);
        for (var frame = 1; frame < 400; frame++)
        {
            Assert.True(effect.Step(Tick(frame * 16)));
        }
    }

    [Fact]
    public void Two_identical_runs_produce_identical_frames()
    {
        byte[] RunOnce()
        {
            var (host, _, effect, _) = Attach();
            using var guard = host;
            effect.Grab(10, 10);
            effect.NotifyMoved(20, 5);
            effect.Release();
            for (var frame = 1; frame <= 10; frame++)
            {
                effect.Step(Tick(frame * 16));
            }

            host.RenderFrame();
            return Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        }

        Assert.Equal(RunOnce(), RunOnce());
    }

    [Fact]
    public void Wobble_deforms_rendered_pixels_and_settling_restores_them()
    {
        var (host, _, effect, _) = Attach();
        using var guard = host;

        host.RenderFrame();
        var before = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);

        effect.Grab(30, 20);
        effect.NotifyMoved(0, 18);
        effect.Release();
        effect.Step(Tick(16));
        host.RenderFrame();
        var during = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.NotEqual(before, during);

        for (var frame = 2; frame < 800 && effect.Step(Tick(frame * 16)); frame++)
        {
        }

        host.RenderFrame();
        var after = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.Equal(before, after);
    }
}

public sealed class WobblyGoldenTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Theory]
    [InlineData(2, "wobbly-frame-2")]
    [InlineData(6, "wobbly-frame-6")]
    [InlineData(20, "wobbly-frame-20")]
    public void Wobbly_animation_frames_are_deterministic(int frames, string golden)
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(50, 34);
        _ = new SceneRect(window, 64, 48, new RenderColor(0.25f, 0.4f, 0.75f, 1f));
        var content = new SceneRect(window, 52, 36, new RenderColor(0.85f, 0.6f, 0.2f, 1f));
        content.SetPosition(6, 6);

        var stack = new TransformStack(window);
        var effect = new WobblyEffect();
        effect.Attach(stack);
        effect.Grab(32, 24);
        effect.NotifyMoved(18, 8);
        effect.Release();
        for (var frame = 1; frame <= frames; frame++)
        {
            effect.Step(Tick(frame * 16));
        }

        host.RenderFrame();
        Golden.AssertMatches(host, golden);
    }
}

public sealed class FireEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void Fire_runs_to_completion_and_heals_the_tree()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(30, 30);
        var content = new SceneRect(window, 60, 40, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        var stack = new TransformStack(window);

        var fire = new FireEffect(new FireOptions { ParticleCount = 64 });
        fire.Begin(stack, hiding: false, Tick(0), 200_000_000);
        Assert.True(fire.IsRunning);

        host.RenderFrame();
        var burning = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);

        var frame = 1;
        while (fire.Step(stack, Tick(frame * 16)) && frame < 2000)
        {
            frame++;
        }

        Assert.False(fire.IsRunning);
        Assert.Same(window, content.Parent);

        host.RenderFrame();
        var done = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Blue(byte[] rgba, int x, int y) => rgba[(((y * host.Target.Width) + x) * 4) + 2];
        Assert.True(Blue(done, 60, 50) > 150, "content fully revealed after the burn");
        _ = burning;
    }

    [Fact]
    public void Fire_reveals_content_progressively()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(30, 30);
        _ = new SceneRect(window, 60, 40, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        var stack = new TransformStack(window);

        var fire = new FireEffect(new FireOptions { ParticleCount = 64 });
        fire.Begin(stack, hiding: false, Tick(0), 400_000_000);
        fire.Step(stack, Tick(30));
        host.RenderFrame();
        var early = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Blue(byte[] rgba, int x, int y) => rgba[(((y * host.Target.Width) + x) * 4) + 2];
        Assert.True(Blue(early, 60, 36) > 150, "top of the window shows early");
        Assert.True(Blue(early, 60, 66) < 100, "bottom of the window is not revealed yet");

        fire.End(stack);
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    public void Fire_runs_on_the_shader_path_and_heals_the_tree(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(FireShader.Source, FireShader.Uniforms);
        Assert.NotNull(shader);

        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(30, 30);
        var content = new SceneRect(window, 60, 40, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        var stack = new TransformStack(window);

        var fire = new FireEffect(new FireOptions { Padding = 20 }) { Shader = shader };
        fire.Begin(stack, hiding: false, Tick(0), 400_000_000);
        Assert.True(fire.IsRunning);

        Assert.True(fire.Step(stack, Tick(30)));
        host.RenderFrame();
        var early = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Sum(byte[] rgba, int x, int y)
        {
            var i = (((y * host.Target.Width) + x) * 4);
            return rgba[i] + rgba[i + 1] + rgba[i + 2];
        }

        Assert.True(Sum(early, 60, 33) > 30, "flames visible at the burn line");

        var frame = 2;
        while (fire.Step(stack, Tick(frame * 16)) && frame < 2000)
        {
            frame++;
        }

        Assert.False(fire.IsRunning);
        Assert.Same(window, content.Parent);
        Assert.Null(stack.Get("fire"));
        window.Destroy();
    }
}

public sealed class OpenCloseAnimationTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void A_snapshot_close_fades_out_and_frees_everything()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(40, 30);
        _ = new SceneRect(window, 60, 40, new RenderColor(0.9f, 0.2f, 0.2f, 1f));

        var snapshot = SceneSnapshot.Capture(window, host.Scene.Root);
        window.Destroy();

        var stack = new TransformStack(snapshot.Tree);
        var animation = new OpenCloseAnimation(OpenCloseKind.Fade, EasingCurve.Linear);
        animation.Begin(stack, hiding: true, Tick(0), 100_000_000);

        Assert.True(animation.Step(stack, Tick(50)));
        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Red(int x, int y) => rgba[((y * host.Target.Width) + x) * 4];
        Assert.InRange(Red(60, 45), 40, 200);

        var frame = 50;
        while (animation.Step(stack, Tick(frame += 16)) && frame < 1000)
        {
        }

        snapshot.Destroy();
        host.RenderFrame();
        rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.True(Red(60, 45) < 30, "the snapshot is gone after the close animation");
    }

    [Fact]
    public void A_null_buffer_unmap_still_snapshots_the_last_frame()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var scene = new SceneSurface(host.Scene.Root, window.ServerSurface);

        SceneSnapshot? snapshot = null;
        window.ServerToplevel.Xdg.Unmapped += () => snapshot = SceneSnapshot.Capture(scene, host.Scene.Root);

        window.Surface.Attach(null, 0, 0);
        window.Surface.Commit();
        host.PumpUntil(() => snapshot is not null);

        Assert.True(snapshot!.NodeCount > 0, "the snapshot captured the frame the client unmapped with");
        Assert.Null(scene.Content.Buffer);

        scene.Destroy();
        snapshot.Destroy();
    }

    [Fact]
    public void A_begin_without_a_tick_anchors_at_the_first_step()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        _ = new SceneRect(window, 40, 30, new RenderColor(1f, 0f, 0f, 1f));
        var stack = new TransformStack(window);

        var animation = new OpenCloseAnimation(OpenCloseKind.Fade, EasingCurve.Linear);
        animation.Begin(stack, hiding: true, 100_000_000);
        var node = stack.Get("open-close");
        Assert.NotNull(node);

        Assert.True(animation.Step(stack, Tick(5000)));
        Assert.InRange(node!.Alpha, 0.82f, 0.85f);

        Assert.True(animation.Step(stack, Tick(5050)));
        Assert.InRange(node.Alpha, 0.32f, 0.35f);

        Assert.True(animation.Step(stack, Tick(5100)));
        Assert.Equal(0f, node.Alpha, 3);
        Assert.False(animation.Step(stack, Tick(5116)));
    }

    [Fact]
    public void Fade_rises_reverses_and_completes()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        _ = new SceneRect(window, 40, 30, new RenderColor(1f, 0f, 0f, 1f));
        var stack = new TransformStack(window);

        var animation = new OpenCloseAnimation(OpenCloseKind.Fade, EasingCurve.Linear);
        animation.Begin(stack, hiding: false, Tick(0), 100_000_000);
        var node = stack.Get("open-close");
        Assert.NotNull(node);

        Assert.True(animation.Step(stack, Tick(30)));
        var rising = node!.Alpha;
        Assert.InRange(rising, 0.2f, 0.4f);

        animation.Reverse(Tick(30));
        Assert.True(animation.Step(stack, Tick(46)));
        Assert.True(node.Alpha < rising + 0.05f);

        var frame = 46;
        while (animation.Step(stack, Tick(frame += 16)) && frame < 1000)
        {
        }

        Assert.True(node.IsDestroyed || node.Alpha <= 0.02f || stack.Get("open-close") is null);
    }

    [Fact]
    public void Completed_open_removes_its_transform()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        var content = new SceneRect(window, 40, 30, new RenderColor(1f, 0f, 0f, 1f));
        var stack = new TransformStack(window);

        var animation = new OpenCloseAnimation(OpenCloseKind.Zoom, EasingCurve.Linear);
        animation.Begin(stack, hiding: false, Tick(0), 100_000_000);
        var frame = 0;
        while (animation.Step(stack, Tick(frame += 16)) && frame < 1000)
        {
        }

        Assert.Null(stack.Get("open-close"));
        Assert.Same(window, content.Parent);
    }

    [Fact]
    public void A_spring_settles_on_its_target()
    {
        var spring = new Spring(300.0, 0.0, 1.0);
        Assert.False(spring.IsDone);

        var nanos = 0L;
        spring.Update(nanos);
        for (var i = 0; i < 500 && !spring.IsDone; i++)
        {
            nanos += 16_666_666L;
            spring.Update(nanos);
        }

        Assert.True(spring.IsDone);
        Assert.Equal(1.0, spring.Current, 2);
    }

    [Fact]
    public void A_stiffer_spring_settles_sooner()
    {
        Assert.True(Spring.SettleMillis(400.0) < Spring.SettleMillis(100.0));
    }

    [Fact]
    public void An_overshooting_spring_passes_its_target()
    {
        var spring = new Spring(400.0, 0.0, 1.0) { Friction = 40.0 };
        var nanos = 0L;
        spring.Update(nanos);
        var peak = 0.0;
        for (var i = 0; i < 400; i++)
        {
            nanos += 8_000_000L;
            spring.Update(nanos);
            peak = Math.Max(peak, spring.Current);
        }

        Assert.True(peak > 1.0);
    }

    [Fact]
    public void A_clamped_spring_never_passes_its_target()
    {
        var spring = new Spring(400.0, 0.0, 1.0) { Friction = 40.0, Clip = SpringClip.Clamp };
        var nanos = 0L;
        spring.Update(nanos);
        for (var i = 0; i < 400; i++)
        {
            nanos += 8_000_000L;
            spring.Update(nanos);
            Assert.True(spring.Current <= 1.0);
        }
    }

    [Fact]
    public void A_large_time_jump_costs_one_second_of_steps()
    {
        var slow = new Spring(300.0, 0.0, 1.0);
        slow.Update(0);
        slow.Update(60_000_000_000L);

        var bounded = new Spring(300.0, 0.0, 1.0);
        bounded.Update(0);
        bounded.Update(1_000_000_000L);

        Assert.Equal(bounded.Current, slow.Current, 6);
    }

    [Fact]
    public void The_spring_easing_runs_from_zero_to_one()
    {
        var easing = EasingCurve.Spring(300.0);
        Assert.Equal(0.0, easing.Apply(0.0), 6);
        Assert.Equal(1.0, easing.Apply(1.0), 2);
        Assert.True(easing.Apply(0.5) > easing.Apply(0.1));
    }

    [Theory]
    [InlineData(1000.0, 4000.0, 0.1)]
    [InlineData(300.0, 1400.0, 0.03)]
    public void A_damped_spring_easing_never_turns_back(double k, double friction, double velocity)
    {
        var easing = EasingCurve.Spring(k, friction, velocity);

        var previous = easing.Apply(0.0);
        for (var i = 1; i <= 400; i++)
        {
            var value = easing.Apply(i / 400.0);
            Assert.True(
                value >= previous - 1e-9,
                $"the curve fell from {previous} to {value} at {i / 400.0}");
            previous = value;
        }

        Assert.True(previous > 0.99, $"the curve ended at {previous}");
    }

    [Fact]
    public void An_underdamped_spring_easing_does_turn_back()
    {
        var easing = EasingCurve.Spring(400.0, 40.0);

        var peak = 0.0;
        var fell = false;
        for (var i = 0; i <= 400; i++)
        {
            var value = easing.Apply(i / 400.0);
            if (value < peak - 1e-6)
            {
                fell = true;
            }

            peak = Math.Max(peak, value);
        }

        Assert.True(peak > 1.0, "an underdamped spring passes its target");
        Assert.True(fell, "and comes back, which is why an opacity easing must be damped");
    }

    [Fact]
    public void A_spring_easing_reports_the_time_it_needs()
    {
        var fade = EasingCurve.Spring(1000.0, 4000.0, 0.1);

        Assert.True(fade.SettleMillis > 0);
        Assert.Equal(fade.SettleMillis, Spring.SettleMillis(1000.0, 4000.0, 0.1), 3);
        Assert.Equal(0.0, EasingCurve.Linear.SettleMillis);
    }
}

public sealed class AnimationDurationTests
{
    [Fact]
    public void The_factor_scales_the_base_duration()
    {
        var duration = new AnimationDuration(250);
        Assert.Equal(250, duration.Millis, 6);
        Assert.Equal(250_000_000, duration.Nanos);
        Assert.False(duration.IsDisabled);

        var slow = duration.WithFactor(2);
        Assert.Equal(500, slow.Millis, 6);
        Assert.Equal(500_000_000, slow.Nanos);
    }

    [Fact]
    public void A_zero_factor_is_off_rather_than_instant()
    {
        var duration = new AnimationDuration(250).WithFactor(0);
        Assert.True(duration.IsDisabled);
        Assert.Equal(0, duration.Nanos);

        Assert.True(new AnimationDuration(0).IsDisabled);
        Assert.True(AnimationDuration.Zero.IsDisabled);
        Assert.True(default(AnimationDuration).IsDisabled);
    }

    [Fact]
    public void A_negative_factor_is_off_too()
    {
        Assert.True(new AnimationDuration(250).WithFactor(-1).IsDisabled);
        Assert.True(new AnimationDuration(-250).IsDisabled);
    }
}

public sealed class MeshGridTests
{
    [Fact]
    public void A_forty_pixel_grid_over_a_full_hd_window_is_kwins()
    {
        var grid = new MeshGrid();
        Assert.True(grid.Layout(new Box(0, 0, 1920, 1080), 40));
        Assert.Equal(48, grid.Columns);
        Assert.Equal(27, grid.Rows);
        Assert.Equal(1296, grid.CellCount);
        Assert.Equal(7776, grid.VertexCount);
        Assert.False(grid.Layout(new Box(0, 0, 1920, 1080), 40));
    }

    [Fact]
    public void The_last_cell_is_clipped_to_the_bounds()
    {
        var grid = new MeshGrid();
        grid.Layout(new Box(10, 20, 100, 100), 40);
        Assert.Equal(3, grid.Columns);
        Assert.Equal(3, grid.Rows);
        Assert.Equal(10, grid.SourceX(0));
        Assert.Equal(50, grid.SourceX(1));
        Assert.Equal(110, grid.SourceX(3));
        Assert.Equal(120, grid.SourceY(3));

        grid.CellSource(grid.CellCount - 1, out var left, out var top, out var right, out var bottom);
        Assert.Equal(90, left);
        Assert.Equal(100, top);
        Assert.Equal(110, right);
        Assert.Equal(120, bottom);
    }

    [Fact]
    public void The_undeformed_grid_writes_source_pixels_for_both_position_and_texture()
    {
        var grid = new MeshGrid();
        grid.Layout(new Box(0, 0, 80, 40), 40);
        var vertices = new MeshVertex[grid.VertexCount];
        grid.Write(vertices);

        Assert.Equal(2, grid.CellCount);
        foreach (var vertex in vertices)
        {
            Assert.Equal(vertex.X, vertex.U);
            Assert.Equal(vertex.Y, vertex.V);
        }

        grid.PointsX[grid.Index(1, 0)] = 100f;
        grid.Write(vertices);
        Assert.Contains(vertices, v => v.X == 100f && v.U == 40f);
    }
}

public sealed class ProjectionFrustumTests
{
    [Fact]
    public void A_zero_angle_and_distance_is_the_identity()
    {
        var rect = new Box(20, 30, 400, 300);
        var transform = Projection.Frustum(rect, FrustumEdge.Top, 0, 0);
        var (x0, y0) = transform.Map(rect.X, rect.Y);
        Assert.Equal(rect.X, x0, 4);
        Assert.Equal(rect.Y, y0, 4);
        var (x1, y1) = transform.Map(rect.Right, rect.Bottom);
        Assert.Equal(rect.Right, x1, 4);
        Assert.Equal(rect.Bottom, y1, 4);
    }

    [Fact]
    public void The_hinge_is_the_rectangles_own_edge_wherever_it_sits()
    {
        var rect = new Box(120, 90, 400, 300);
        var transform = Projection.Frustum(rect, FrustumEdge.Top, 8, 0);
        var (hingeX, hingeY) = transform.Map(rect.X, rect.Y);
        Assert.Equal(rect.X, hingeX, 4);
        Assert.Equal(rect.Y, hingeY, 4);

        var (_, farY) = transform.Map(rect.X, rect.Bottom);
        Assert.True(farY < rect.Bottom, $"the far edge rotates back toward the hinge, got {farY}");
    }

    [Fact]
    public void The_hinged_edge_stays_put_and_the_far_edge_moves()
    {
        var rect = new Box(0, 0, 400, 300);
        var transform = Projection.Frustum(rect, FrustumEdge.Top, 8, 0);
        var (hingeX, hingeY) = transform.Map(0, 0);
        Assert.Equal(0, hingeX, 4);
        Assert.Equal(0, hingeY, 4);

        var (_, farY) = transform.Map(0, 300);
        Assert.True(farY < 300, $"the far edge rotates back toward the hinge, got {farY}");
    }

    [Fact]
    public void Distance_pushes_the_whole_window_away_from_the_centre_of_the_rect()
    {
        var rect = new Box(0, 0, 400, 300);
        var near = Projection.Frustum(rect, FrustumEdge.Top, 0, 0);
        var far = Projection.Frustum(rect, FrustumEdge.Top, 0, 30);
        var (nearX, nearY) = near.Map(0, 0);
        var (farX, farY) = far.Map(0, 0);
        Assert.True(farX > nearX, "a pushed-back corner shrinks toward the centre");
        Assert.True(farY > nearY, "a pushed-back corner shrinks toward the centre");
    }

    [Fact]
    public void Each_edge_hinges_on_its_own_side()
    {
        var rect = new Box(0, 0, 400, 300);
        var (bottomX, bottomY) = Projection.Frustum(rect, FrustumEdge.Bottom, 8, 0).Map(0, 300);
        Assert.Equal(0, bottomX, 4);
        Assert.Equal(300, bottomY, 4);

        var (leftX, leftY) = Projection.Frustum(rect, FrustumEdge.Left, 8, 0).Map(0, 0);
        Assert.Equal(0, leftX, 4);
        Assert.Equal(0, leftY, 4);

        var (rightX, rightY) = Projection.Frustum(rect, FrustumEdge.Right, 8, 0).Map(400, 0);
        Assert.Equal(400, rightX, 4);
        Assert.Equal(0, rightY, 4);
    }
}

public sealed class BlurStrengthTests
{
    [Fact]
    public void The_fifteen_steps_are_kwins()
    {
        (int Iterations, double Offset, int Expand)[] expected =
        [
            (1, 1.5, 10), (1, 2.0, 10),
            (2, 2.5, 20), (2, 3.0, 20),
            (3, 2.6, 50), (3, 3.2, 50), (3, 3.8, 50), (3, 4.4, 50), (3, 5.0, 50),
            (4, 3.0 + (5.0 / 6.0), 150),
            (4, 3.0 + (10.0 / 6.0), 150),
            (4, 3.0 + (15.0 / 6.0), 150),
            (4, 3.0 + (20.0 / 6.0), 150),
            (4, 3.0 + (25.0 / 6.0), 150),
            (4, 8.0, 150),
        ];

        for (var step = 1; step <= BlurStrength.Steps; step++)
        {
            var value = BlurStrength.For(step);
            Assert.Equal(expected[step - 1].Iterations, value.Iterations);
            Assert.Equal(expected[step - 1].Offset, value.Offset, 6);
            Assert.Equal(expected[step - 1].Expand, value.ExpandSize);
        }
    }

    [Fact]
    public void An_out_of_range_strength_clamps()
    {
        Assert.Equal(BlurStrength.For(1), BlurStrength.For(0));
        Assert.Equal(BlurStrength.For(1), BlurStrength.For(-4));
        Assert.Equal(BlurStrength.For(15), BlurStrength.For(99));
    }
}

public sealed class BlurColorMatrixTests
{
    private static (double R, double G, double B) Apply(double saturation, double contrast, double r, double g, double b)
    {
        var matrix = new float[BlurColorMatrix.Length];
        BlurColorMatrix.Build(saturation, contrast, matrix);
        double Row(int output) =>
            (r * matrix[(output * 4) + 0]) + (g * matrix[(output * 4) + 1]) +
            (b * matrix[(output * 4) + 2]) + matrix[(output * 4) + 3];
        return (Row(0), Row(1), Row(2));
    }

    [Fact]
    public void Unit_saturation_and_contrast_is_the_identity()
    {
        var (r, g, b) = Apply(1.0, 1.0, 0.2, 0.4, 0.6);
        Assert.Equal(0.2, r, 5);
        Assert.Equal(0.4, g, 5);
        Assert.Equal(0.6, b, 5);
    }

    [Fact]
    public void Zero_saturation_is_rec_709_luma()
    {
        var (r, g, b) = Apply(0.0, 1.0, 0.2, 0.4, 0.6);
        var luma = (0.2 * 0.2126) + (0.4 * 0.7152) + (0.6 * 0.0722);
        Assert.Equal(luma, r, 5);
        Assert.Equal(luma, g, 5);
        Assert.Equal(luma, b, 5);
    }

    [Fact]
    public void Contrast_pivots_about_a_half()
    {
        var (r, _, _) = Apply(1.0, 0.5, 0.5, 0.5, 0.5);
        Assert.Equal(0.5, r, 5);

        var (dark, _, _) = Apply(1.0, 0.5, 0.0, 0.0, 0.0);
        Assert.Equal(0.25, dark, 5);
    }

    [Fact]
    public void The_default_saturation_of_one_and_a_half_lifts_a_saturated_colour()
    {
        var (r, _, b) = Apply(1.5, 1.0, 0.8, 0.2, 0.2);
        Assert.True(r > 0.8, $"the dominant channel rises, got {r}");
        Assert.True(b < 0.2, $"the others fall, got {b}");
    }
}

public sealed class BlurNoiseTests
{
    [Fact]
    public void The_noise_is_bounded_by_the_strength_and_reproducible()
    {
        var first = new byte[BlurNoise.Size * BlurNoise.Size];
        var second = new byte[BlurNoise.Size * BlurNoise.Size];
        BlurNoise.Fill(first, 5);
        BlurNoise.Fill(second, 5);
        Assert.Equal(first, second);
        Assert.All(first, value => Assert.InRange(value, (byte)0, (byte)4));
        Assert.Contains(first, value => value != first[0]);
    }

    [Fact]
    public void A_zero_strength_is_a_blank_texture()
    {
        var pixels = new byte[BlurNoise.Size * BlurNoise.Size];
        Array.Fill(pixels, (byte)7);
        BlurNoise.Fill(pixels, 0);
        Assert.All(pixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void A_different_seed_gives_different_noise()
    {
        var first = new byte[BlurNoise.Size * BlurNoise.Size];
        var second = new byte[BlurNoise.Size * BlurNoise.Size];
        BlurNoise.Fill(first, 64);
        BlurNoise.Fill(second, 64, seed: 12345);
        Assert.NotEqual(first, second);
    }
}

public sealed class GlideEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack) Window()
    {
        var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        _ = new SceneRect(tree, 400, 300, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        return (host, new TransformStack(tree));
    }

    [Fact]
    public void An_opening_window_starts_tilted_and_dim_and_lands_square()
    {
        var (scene, stack) = Window();
        var glide = new GlideEffect();
        Assert.True(glide.Begin(stack, hiding: false, Tick(0), new AnimationDuration(160)));

        var node = stack.Get("glide");
        Assert.NotNull(node);
        Assert.Equal(0.4f, node!.Alpha, 3);
        Assert.False(node.Matrix.IsIdentity);

        Assert.True(glide.Step(stack, Tick(80)));
        Assert.True(node.Alpha > 0.4f && node.Alpha < 1f, $"midpoint opacity {node.Alpha}");

        glide.Step(stack, Tick(200));
        Assert.False(glide.Step(stack, Tick(220)));
        Assert.Null(stack.Get("glide"));
        scene.Dispose();
    }

    [Fact]
    public void A_closing_window_keeps_its_node_and_fades_out()
    {
        var (scene, stack) = Window();
        var glide = new GlideEffect();
        Assert.True(glide.Begin(stack, hiding: true, Tick(0), new AnimationDuration(160)));

        var node = stack.Get("glide");
        Assert.NotNull(node);
        Assert.Equal(1f, node!.Alpha, 3);

        glide.Step(stack, Tick(200));
        Assert.False(glide.Step(stack, Tick(220)));
        Assert.Equal(0f, node.Alpha, 3);
        Assert.NotNull(stack.Get("glide"));

        glide.End(stack);
        Assert.Null(stack.Get("glide"));
        scene.Dispose();
    }

    [Fact]
    public void A_zero_duration_does_not_run_at_all()
    {
        var (scene, stack) = Window();
        var glide = new GlideEffect();
        Assert.False(glide.Begin(stack, hiding: false, Tick(0), new AnimationDuration(160).WithFactor(0)));
        Assert.Null(stack.Get("glide"));
        Assert.False(glide.IsRunning);
        scene.Dispose();
    }

    [Fact]
    public void Reverse_flips_the_direction_mid_flight()
    {
        var (scene, stack) = Window();
        var glide = new GlideEffect();
        glide.Begin(stack, hiding: false, Tick(0), new AnimationDuration(160));
        glide.Step(stack, Tick(80));
        Assert.False(glide.IsHiding);

        glide.Reverse(Tick(80));
        Assert.True(glide.IsHiding);
        Assert.True(glide.Step(stack, Tick(100)));
        scene.Dispose();
    }
}

public sealed class SheetEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack) Dialog()
    {
        var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        tree.SetPosition(200, 400);
        _ = new SceneRect(tree, 300, 200, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        return (host, new TransformStack(tree));
    }

    [Fact]
    public void A_sheet_opens_flat_and_lands_upright()
    {
        var (host, stack) = Dialog();
        var sheet = new SheetEffect();
        Assert.True(sheet.Begin(stack, hiding: false, parentDrop: 120, Tick(0), new AnimationDuration(300)));

        var node = stack.Get("sheet");
        Assert.NotNull(node);
        Assert.Equal(0f, node!.Alpha, 3);

        Assert.True(sheet.Step(stack, Tick(150)));
        Assert.True(node.Alpha > 0.4f && node.Alpha < 0.6f, $"halfway opacity {node.Alpha}");
        var (_, midY) = node.Matrix.Map(0, 200);
        Assert.True(midY < 200, $"the hinged sheet is still folded back at the halfway point, got {midY}");
        var (hingeX, hingeY) = node.Matrix.Map(0, 0);
        Assert.Equal(0, hingeX, 3);
        Assert.True(hingeY < 0, $"the top edge rides up with the parent drop, got {hingeY}");

        sheet.Step(stack, Tick(320));
        Assert.False(sheet.Step(stack, Tick(340)));
        Assert.Null(stack.Get("sheet"));
        host.Dispose();
    }

    [Fact]
    public void A_closing_sheet_runs_the_timeline_backwards_and_keeps_its_node()
    {
        var (host, stack) = Dialog();
        var sheet = new SheetEffect();
        Assert.True(sheet.Begin(stack, hiding: true, parentDrop: 0, Tick(0), new AnimationDuration(300)));

        var node = stack.Get("sheet");
        Assert.NotNull(node);
        Assert.Equal(1f, node!.Alpha, 3);

        sheet.Step(stack, Tick(320));
        Assert.False(sheet.Step(stack, Tick(340)));
        Assert.Equal(0f, node.Alpha, 3);
        Assert.NotNull(stack.Get("sheet"));

        sheet.End(stack);
        Assert.Null(stack.Get("sheet"));
        host.Dispose();
    }

    [Fact]
    public void A_zero_duration_does_not_run()
    {
        var (host, stack) = Dialog();
        var sheet = new SheetEffect();
        Assert.False(sheet.Begin(stack, hiding: false, 0, Tick(0), new AnimationDuration(300).WithFactor(0)));
        Assert.Null(stack.Get("sheet"));
        host.Dispose();
    }
}

public sealed class MagicLampEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack, SceneTree Tree) Window()
    {
        var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        tree.SetPosition(300, 200);
        _ = new SceneRect(tree, 400, 300, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        return (host, new TransformStack(tree), tree);
    }

    private static MeshVertex[] Vertices(MagicLampEffect lamp, Box bounds)
    {
        var vertices = new MeshVertex[lamp.VertexCount(bounds)];
        lamp.WriteVertices(bounds, vertices);
        return vertices;
    }

    [Fact]
    public void At_rest_the_grid_is_the_window()
    {
        var (host, stack, _) = Window();
        var lamp = new MagicLampEffect();
        var window = new Box(300, 200, 400, 300);
        var icon = new Box(400, 1040, 60, 30);
        Assert.True(lamp.Begin(stack, window, icon, MinimizeEdge.Bottom, restoring: false, Tick(0), new AnimationDuration(250)));

        var bounds = new Box(0, 0, 400, 300);
        var vertices = Vertices(lamp, bounds);
        Assert.Equal(10 * 8 * 6, vertices.Length);
        foreach (var vertex in vertices)
        {
            Assert.Equal(vertex.U, vertex.X, 3);
            Assert.Equal(vertex.V, vertex.Y, 3);
        }

        host.Dispose();
    }

    [Fact]
    public void The_top_of_the_window_lags_behind_the_bottom()
    {
        var (host, stack, _) = Window();
        var lamp = new MagicLampEffect();
        var window = new Box(300, 200, 400, 300);
        var icon = new Box(400, 1040, 60, 30);
        lamp.Begin(stack, window, icon, MinimizeEdge.Bottom, restoring: false, Tick(0), new AnimationDuration(250));
        lamp.Step(Tick(125));

        var bounds = new Box(0, 0, 400, 300);
        var vertices = Vertices(lamp, bounds);
        var topRow = vertices.Where(v => v.V == 0f).ToArray();
        var bottomRow = vertices.Where(v => v.V == 300f).ToArray();
        Assert.NotEmpty(topRow);
        Assert.NotEmpty(bottomRow);

        var topTravel = topRow.Max(v => v.Y - v.V);
        var bottomTravel = bottomRow.Max(v => v.Y - v.V);
        Assert.True(bottomTravel > topTravel, $"bottom {bottomTravel} should outrun top {topTravel}");
        Assert.True(topTravel >= 0);
        host.Dispose();
    }

    [Fact]
    public void The_window_pinches_toward_the_icon_horizontally()
    {
        var (host, stack, _) = Window();
        var lamp = new MagicLampEffect();
        var window = new Box(300, 200, 400, 300);
        var icon = new Box(400, 1040, 60, 30);
        lamp.Begin(stack, window, icon, MinimizeEdge.Bottom, restoring: false, Tick(0), new AnimationDuration(250));
        lamp.Step(Tick(240));

        var bounds = new Box(0, 0, 400, 300);
        var vertices = Vertices(lamp, bounds);
        var bottomRow = vertices.Where(v => v.V == 300f).ToArray();
        var spread = bottomRow.Max(v => v.X) - bottomRow.Min(v => v.X);
        Assert.True(spread < 400f, $"the leading edge narrows toward the icon, got {spread}");
        host.Dispose();
    }

    [Fact]
    public void Each_edge_moves_the_window_the_way_the_icon_lies()
    {
        var (host, stack, _) = Window();
        var window = new Box(300, 200, 400, 300);
        var bounds = new Box(0, 0, 400, 300);

        double Travel(MinimizeEdge edge, Box icon, bool horizontal)
        {
            var lamp = new MagicLampEffect();
            lamp.Begin(stack, window, icon, edge, restoring: false, Tick(0), new AnimationDuration(250));
            lamp.Step(Tick(125));
            var vertices = Vertices(lamp, bounds);
            var travel = horizontal
                ? vertices.Average(v => v.X - v.U)
                : vertices.Average(v => v.Y - v.V);
            lamp.End(stack);
            return travel;
        }

        Assert.True(Travel(MinimizeEdge.Bottom, new Box(400, 1040, 60, 30), horizontal: false) > 0);
        Assert.True(Travel(MinimizeEdge.Top, new Box(400, 0, 60, 30), horizontal: false) < 0);
        Assert.True(Travel(MinimizeEdge.Right, new Box(1880, 300, 30, 60), horizontal: true) > 0);
        Assert.True(Travel(MinimizeEdge.Left, new Box(0, 300, 30, 60), horizontal: true) < 0);
        host.Dispose();
    }

    [Fact]
    public void The_fallback_target_is_the_nearest_border_under_the_cursor()
    {
        var window = new Box(100, 100, 400, 300);

        var (icon, edge) = MagicLampEffect.FallbackTarget(window, 120, 300);
        Assert.Equal(MinimizeEdge.Left, edge);
        Assert.Equal(100, icon.X);
        Assert.True(icon.IsEmpty);

        (icon, edge) = MagicLampEffect.FallbackTarget(window, 300, 120);
        Assert.Equal(MinimizeEdge.Top, edge);
        Assert.Equal(100, icon.Y);

        (_, edge) = MagicLampEffect.FallbackTarget(window, 300, 900);
        Assert.Equal(MinimizeEdge.Bottom, edge);

        (_, edge) = MagicLampEffect.FallbackTarget(window, 900, 300);
        Assert.Equal(MinimizeEdge.Right, edge);
    }

    [Fact]
    public void A_zero_duration_does_not_run()
    {
        var (host, stack, _) = Window();
        var lamp = new MagicLampEffect();
        Assert.False(lamp.Begin(
            stack, new Box(300, 200, 400, 300), new Box(400, 1040, 60, 30), MinimizeEdge.Bottom,
            restoring: false, new AnimationDuration(250).WithFactor(0)));
        Assert.Null(stack.Get("magiclamp"));
        host.Dispose();
    }
}

public sealed class FallApartEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack) Window()
    {
        var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        _ = new SceneRect(tree, 400, 300, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        return (host, new TransformStack(tree));
    }

    private static MeshVertex[] Vertices(FallApartEffect effect, Box bounds)
    {
        var vertices = new MeshVertex[effect.VertexCount(bounds)];
        effect.WriteVertices(bounds, vertices);
        return vertices;
    }

    [Fact]
    public void At_rest_every_cell_sits_on_its_own_source_rectangle()
    {
        var (host, stack) = Window();
        var effect = new FallApartEffect();
        Assert.True(effect.Begin(stack, Tick(0), new AnimationDuration(1000)));

        var bounds = new Box(0, 0, 400, 300);
        foreach (var vertex in Vertices(effect, bounds))
        {
            Assert.Equal(vertex.U, vertex.X, 3);
            Assert.Equal(vertex.V, vertex.Y, 3);
        }

        host.Dispose();
    }

    [Fact]
    public void The_cells_fly_outward_and_the_window_fades()
    {
        var (host, stack) = Window();
        var effect = new FallApartEffect();
        effect.Begin(stack, Tick(0), new AnimationDuration(1000));
        var node = stack.Get("fallapart");
        Assert.NotNull(node);

        Assert.True(effect.Step(Tick(900)));
        Assert.True(node!.Alpha < 0.3f, $"the window has nearly gone, alpha {node.Alpha}");

        var bounds = new Box(0, 0, 400, 300);
        var vertices = Vertices(effect, bounds);
        var moved = vertices.Count(v => Math.Abs(v.X - v.U) > 1f || Math.Abs(v.Y - v.V) > 1f);
        Assert.True(moved > vertices.Length / 2, $"only {moved} of {vertices.Length} vertices moved");

        var left = vertices.Where(v => v.U < 100f).Average(v => v.X - v.U);
        var right = vertices.Where(v => v.U > 300f).Average(v => v.X - v.U);
        Assert.True(left < right, $"left pieces {left} should go left of right pieces {right}");
        host.Dispose();
    }

    [Fact]
    public void A_cell_flies_the_same_way_every_frame()
    {
        var (host, stack) = Window();
        var effect = new FallApartEffect();
        effect.Begin(stack, Tick(0), new AnimationDuration(1000));
        var bounds = new Box(0, 0, 400, 300);

        effect.Step(Tick(500));
        var early = Vertices(effect, bounds);
        effect.Step(Tick(600));
        var later = Vertices(effect, bounds);

        var earlyOffset = early[0].X - early[0].U;
        var laterOffset = later[0].X - later[0].U;
        Assert.True(Math.Sign(earlyOffset) == Math.Sign(laterOffset) || earlyOffset == 0,
            $"cell zero reversed direction: {earlyOffset} then {laterOffset}");
        Assert.True(Math.Abs(laterOffset) > Math.Abs(earlyOffset), "the cell keeps travelling");

        var second = new FallApartEffect();
        second.Begin(new TransformStack(new SceneTree(host.Scene.Root)), Tick(0), new AnimationDuration(1000));
        second.Step(Tick(500));
        var repeat = Vertices(second, bounds);
        Assert.Equal(early[0].X, repeat[0].X, 4);
        Assert.Equal(early[0].Y, repeat[0].Y, 4);
        host.Dispose();
    }

    [Fact]
    public void A_zero_duration_does_not_run()
    {
        var (host, stack) = Window();
        var effect = new FallApartEffect();
        Assert.False(effect.Begin(stack, new AnimationDuration(1000).WithFactor(0)));
        Assert.Null(stack.Get("fallapart"));
        host.Dispose();
    }
}

public sealed class SquashEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack) Window()
    {
        var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        tree.SetPosition(300, 200);
        _ = new SceneRect(tree, 400, 300, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        return (host, new TransformStack(tree));
    }

    [Fact]
    public void A_minimize_shrinks_the_window_onto_its_taskbar_entry()
    {
        var (host, stack) = Window();
        var squash = new SquashEffect();
        var window = new Box(300, 200, 400, 300);
        var icon = new Box(900, 1040, 60, 30);
        Assert.True(squash.Begin(stack, window, icon, restoring: false, Tick(0), new AnimationDuration(250)));

        var node = stack.Get("squash");
        Assert.NotNull(node);
        Assert.Equal(1f, node!.Alpha, 3);
        Assert.True(node.Matrix.IsIdentity);

        squash.Step(stack, Tick(260));
        Assert.False(squash.Step(stack, Tick(280)));
        Assert.Equal(0f, node.Alpha, 3);

        var (x0, y0) = node.Matrix.Map(0, 0);
        var (x1, y1) = node.Matrix.Map(400, 300);
        Assert.Equal(60, x1 - x0, 2);
        Assert.Equal(30, y1 - y0, 2);
        Assert.Equal(icon.X - window.X, x0, 2);
        Assert.Equal(icon.Y - window.Y, y0, 2);
        Assert.NotNull(stack.Get("squash"));
        host.Dispose();
    }

    [Fact]
    public void A_restore_runs_it_backwards_and_heals_the_stack()
    {
        var (host, stack) = Window();
        var squash = new SquashEffect();
        var window = new Box(300, 200, 400, 300);
        var icon = new Box(900, 1040, 60, 30);
        Assert.True(squash.Begin(stack, window, icon, restoring: true, Tick(0), new AnimationDuration(250)));

        var node = stack.Get("squash");
        Assert.NotNull(node);
        Assert.Equal(0f, node!.Alpha, 3);

        squash.Step(stack, Tick(260));
        Assert.False(squash.Step(stack, Tick(280)));
        Assert.Null(stack.Get("squash"));
        host.Dispose();
    }

    [Fact]
    public void Without_an_icon_it_does_not_run()
    {
        var (host, stack) = Window();
        var squash = new SquashEffect();
        Assert.False(squash.Begin(
            stack, new Box(300, 200, 400, 300), default, restoring: false, Tick(0), new AnimationDuration(250)));
        Assert.Null(stack.Get("squash"));
        host.Dispose();
    }
}

public sealed class OpenCloseScaleTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void The_in_and_out_scales_are_separate()
    {
        using var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        _ = new SceneRect(tree, 400, 300, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        var stack = new TransformStack(tree);

        var animation = new OpenCloseAnimation(OpenCloseKind.Zoom) { InScale = 0.8, OutScale = 0.5 };
        Assert.True(animation.Begin(stack, hiding: false, Tick(0), new AnimationDuration(200)));
        var node = stack.Get("open-close");
        Assert.NotNull(node);

        var (left, _) = node!.Matrix.Map(0, 0);
        var (right, _) = node.Matrix.Map(400, 0);
        Assert.Equal(320, right - left, 2);

        animation.Step(stack, Tick(220));
        animation.Step(stack, Tick(240));
        Assert.Null(stack.Get("open-close"));

        var closing = new OpenCloseAnimation(OpenCloseKind.Zoom) { InScale = 0.8, OutScale = 0.5 };
        Assert.True(closing.Begin(stack, hiding: true, Tick(300), new AnimationDuration(200)));
        var closeNode = stack.Get("open-close");
        Assert.NotNull(closeNode);
        closing.Step(stack, Tick(520));
        closing.Step(stack, Tick(540));
        var (outLeft, _) = closeNode!.Matrix.Map(0, 0);
        var (outRight, _) = closeNode.Matrix.Map(400, 0);
        Assert.Equal(200, outRight - outLeft, 2);
    }

    [Fact]
    public void A_zero_duration_does_not_run()
    {
        using var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        var stack = new TransformStack(tree);
        var animation = new OpenCloseAnimation(OpenCloseKind.Fade);
        Assert.False(animation.Begin(stack, hiding: false, Tick(0), new AnimationDuration(150).WithFactor(0)));
        Assert.Null(stack.Get("open-close"));
    }
}

public sealed class MagnifierStageTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void The_zoom_ramps_toward_its_target_and_stops_there()
    {
        var stage = new MagnifierStage();
        Assert.Equal(1.0, stage.Zoom, 6);
        Assert.False(stage.IsActive);

        stage.ZoomIn();
        Assert.Equal(1.2, stage.TargetZoom, 6);
        Assert.True(stage.IsActive);

        stage.Step(Tick(0));
        stage.Step(Tick(16));
        Assert.True(stage.Zoom > 1.0 && stage.Zoom <= 1.2, $"zoom {stage.Zoom}");

        for (var frame = 2; frame < 60; frame++)
        {
            stage.Step(Tick(frame * 16));
        }

        Assert.Equal(1.2, stage.Zoom, 6);
        Assert.True(stage.IsActive, "a settled lens is still drawing");

        stage.Reset();
        for (var frame = 0; frame < 120; frame++)
        {
            stage.Step(Tick(1000 + (frame * 16)));
        }

        Assert.Equal(1.0, stage.Zoom, 6);
        Assert.False(stage.IsActive);
    }

    [Fact]
    public void Zooming_out_never_goes_below_one()
    {
        var stage = new MagnifierStage();
        stage.ZoomOut();
        Assert.Equal(1.0, stage.TargetZoom, 6);
        Assert.False(stage.IsActive);
    }

    [Fact]
    public void The_lens_draws_only_once_zoomed()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.2f, 0.6f, 0.9f, 1f));
        var stage = new MagnifierStage(new MagnifierOptions { Width = 40, Height = 40, FrameWidth = 2 })
        {
            CenterX = 80,
            CenterY = 60,
        };
        sceneOutput.AddPostStage(stage);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)state.Buffer!);
        int Blue(int x, int y) => rgba[(((y * 160) + x) * 4) + 2];
        Assert.True(Blue(80, 60) > 200, "the unzoomed frame is the plain scene");

        stage.TargetZoom = 3;
        stage.Step(Tick(0));
        stage.Step(Tick(2000));
        Assert.Equal(3, stage.Zoom, 6);

        sceneOutput.Ring.AddWhole();
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        rgba = Basin.Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)state.Buffer!);
        Assert.True(Blue(80, 39) < 40, "the lens frame is black above the lens");
        Assert.True(Blue(80, 60) > 200, "the lens itself still shows the scene");
    }
}

public sealed class ColorBlindnessStageTests
{
    private sealed class BufferGuard(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }

    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    private static (byte R, byte G, byte B) Through(CompositorTestHost host, ColorBlindnessStage stage, RenderColor input)
    {
        var source = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        using var sourceGuard = new BufferGuard(source);
        var fill = host.Renderer.BeginBufferPass(source, new RenderPassOptions());
        fill.AddRect(input, new Box(0, 0, 32, 32));
        Assert.True(fill.Submit());

        using var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);
        var target = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        using var targetGuard = new BufferGuard(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        stage.Render(pass, texture!, new PostContext(32, 32, default));
        Assert.True(pass.Submit());

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        var index = ((16 * 32) + 16) * 4;
        return (rgba[index], rgba[index + 1], rgba[index + 2]);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Protanopia_pushes_the_lost_red_into_green_and_blue(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(
            ColorBlindnessShader.Source, ColorBlindnessShader.Uniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the correction shader");

        var stage = new ColorBlindnessStage(shader) { Mode = ColorBlindnessMode.Protanopia };
        var red = new RenderColor(0.8f, 0.1f, 0.1f, 1f);
        var plain = Through(host, new ColorBlindnessStage(null), red);
        var corrected = Through(host, stage, red);

        Assert.True(corrected.G > plain.G + 4, $"green {corrected.G} should rise above {plain.G}");
        Assert.True(corrected.B > plain.B + 4, $"blue {corrected.B} should rise above {plain.B}");
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Monochrome_flattens_every_channel(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(
            ColorBlindnessShader.Source, ColorBlindnessShader.Uniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the correction shader");

        var stage = new ColorBlindnessStage(shader) { Mode = ColorBlindnessMode.Monochrome };
        var (r, g, b) = Through(host, stage, new RenderColor(0.8f, 0.2f, 0.4f, 1f));
        Assert.True(Math.Abs(r - g) <= 3 && Math.Abs(g - b) <= 3, $"channels {r},{g},{b} should be equal");
    }

    [Fact]
    public void Without_a_shader_the_stage_passes_the_frame_through()
    {
        using var host = new CompositorTestHost();
        var stage = new ColorBlindnessStage(null);
        Assert.False(stage.IsSupported);
        var (r, g, b) = Through(host, stage, new RenderColor(0.8f, 0.2f, 0.4f, 1f));
        Assert.True(r > 190 && g > 40 && g < 70 && b > 90 && b < 120, $"passed through as {r},{g},{b}");
    }
}

public sealed class ZoomStageTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private sealed class Focus(Box rectangle, long nanos) : IZoomTarget
    {
        public bool TryGetFocus(out Box rect, out long reportedAtNanos)
        {
            rect = rectangle;
            reportedAtNanos = nanos;
            return true;
        }
    }

    private static ZoomStage Settled(ZoomOptions options, double zoom)
    {
        var stage = new ZoomStage(null, options);
        stage.ZoomTo(zoom);
        stage.Step(Tick(0), 1920, 1080);
        stage.Step(Tick(100000), 1920, 1080);
        Assert.Equal(zoom, stage.Zoom, 6);
        return stage;
    }

    [Fact]
    public void Proportional_tracking_keeps_the_cursor_where_it_is()
    {
        var stage = Settled(new ZoomOptions { MouseTracking = ZoomTracking.Proportional }, 2.0);
        stage.SetCursor(400, 300, 0);
        stage.Step(Tick(100016), 1920, 1080);

        Assert.Equal(-400, stage.TranslationX, 6);
        Assert.Equal(-300, stage.TranslationY, 6);
        Assert.Equal(400, (400 * stage.Zoom) + stage.TranslationX, 6);
    }

    [Fact]
    public void Centred_tracking_pins_the_cursor_to_the_middle_and_clamps_at_the_edges()
    {
        var stage = Settled(new ZoomOptions { MouseTracking = ZoomTracking.Centered }, 2.0);
        stage.SetCursor(960, 540, 0);
        stage.Step(Tick(100016), 1920, 1080);
        Assert.Equal(-960, stage.TranslationX, 6);

        stage.SetCursor(10, 10, 0);
        stage.Step(Tick(100032), 1920, 1080);
        Assert.Equal(0, stage.TranslationX, 6);
        Assert.Equal(0, stage.TranslationY, 6);

        stage.SetCursor(1910, 1070, 0);
        stage.Step(Tick(100048), 1920, 1080);
        Assert.Equal(-1920, stage.TranslationX, 6);
        Assert.Equal(-1080, stage.TranslationY, 6);
    }

    [Fact]
    public void Strict_centring_does_not_clamp()
    {
        var stage = Settled(new ZoomOptions { MouseTracking = ZoomTracking.CenteredStrict }, 2.0);
        stage.SetCursor(10, 10, 0);
        stage.Step(Tick(100016), 1920, 1080);
        Assert.Equal(940, stage.TranslationX, 6);
        Assert.Equal(520, stage.TranslationY, 6);
    }

    [Fact]
    public void Disabled_tracking_ignores_the_cursor()
    {
        var stage = Settled(new ZoomOptions { MouseTracking = ZoomTracking.Disabled }, 2.0);
        stage.SetCursor(1900, 1000, 0);
        stage.Step(Tick(100016), 1920, 1080);
        Assert.Equal(0, stage.TranslationX, 6);
        Assert.Equal(0, stage.TranslationY, 6);
    }

    [Fact]
    public void Push_tracking_only_moves_at_the_edge()
    {
        var stage = Settled(new ZoomOptions { MouseTracking = ZoomTracking.Push }, 2.0);
        stage.SetCursor(960, 540, 0);
        for (var frame = 1; frame <= 200; frame++)
        {
            stage.Step(Tick(100000 + (frame * 16)), 1920, 1080);
        }

        var restX = stage.TranslationX;
        stage.Step(Tick(200000), 1920, 1080);
        Assert.Equal(restX, stage.TranslationX, 6);

        stage.SetCursor(1919, 540, 0);
        stage.Step(Tick(200016), 1920, 1080);
        Assert.True(stage.TranslationX < restX, $"the view pushed left, {stage.TranslationX} vs {restX}");
    }

    [Fact]
    public void Focus_tracking_takes_over_when_the_cursor_has_been_still()
    {
        var options = new ZoomOptions
        {
            MouseTracking = ZoomTracking.Disabled,
            FocusTracking = true,
            FocusDelayMillis = 350,
        };
        var stage = Settled(options, 2.0);
        stage.SetCursor(100, 100, 0);
        stage.Target = new Focus(new Box(1000, 600, 40, 20), 1_000_000_000);
        stage.Step(Tick(100016), 1920, 1080);

        Assert.Equal(Math.Min(0, Math.Max(-1920, (int)(960 - (1020 * 2.0)))), stage.TranslationX, 6);
    }

    [Fact]
    public void The_pixel_grid_switches_on_above_its_threshold()
    {
        var stage = Settled(new ZoomOptions { PixelGridZoom = 15.0 }, 14.0);
        Assert.False(stage.DrawsPixelGrid);
        stage.ZoomTo(16.0);
        stage.Step(Tick(200000), 1920, 1080);
        Assert.True(stage.DrawsPixelGrid);
    }

    [Fact]
    public void Zooming_out_snaps_to_one_near_the_bottom()
    {
        var stage = new ZoomStage(null);
        stage.ZoomIn();
        Assert.Equal(1.2, stage.TargetZoom, 6);
        stage.ZoomOut();
        Assert.Equal(1.0, stage.TargetZoom, 6);
    }
}

public sealed class CrossfadeStageTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void Blend_changes_fades_the_captured_frame_out_over_the_new_one()
    {
        using var host = new CompositorTestHost();
        using var stage = new BlendChangesStage(host.Renderer);
        var previous = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var fill = host.Renderer.BeginBufferPass(previous, new RenderPassOptions());
        fill.AddRect(new RenderColor(1f, 0f, 0f, 1f), new Box(0, 0, 32, 32));
        Assert.True(fill.Submit());

        Assert.True(stage.Begin(previous, Tick(0), new AnimationDuration(400)));
        Assert.True(stage.IsRunning);
        Assert.Equal(0, stage.Progress, 6);

        var target = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var frameBuffer = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var frameFill = host.Renderer.BeginBufferPass(frameBuffer, new RenderPassOptions());
        frameFill.AddRect(new RenderColor(0f, 0f, 1f, 1f), new Box(0, 0, 32, 32));
        Assert.True(frameFill.Submit());
        using var frameTexture = host.Renderer.ImportTexture(frameBuffer);

        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        stage.Render(pass, frameTexture!, new PostContext(32, 32, Tick(0)));
        Assert.True(pass.Submit());
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        Assert.True(rgba[0] > 200, "the captured frame covers the new one at the start");

        stage.Step(Tick(420));
        Assert.False(stage.Step(Tick(440)));
        Assert.False(stage.IsRunning);

        pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        stage.Render(pass, frameTexture!, new PostContext(32, 32, Tick(440)));
        Assert.True(pass.Submit());
        rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        Assert.True(rgba[2] > 200 && rgba[0] < 60, "only the new frame is left");

        target.Destroy();
        previous.Destroy();
        frameBuffer.Destroy();
    }

    [Fact]
    public void The_transform_angle_is_the_short_way_round()
    {
        Assert.Equal(90, ScreenTransformAnimation.AngleBetween(OutputTransform.Normal, OutputTransform.Rotate90), 6);
        Assert.Equal(-90, ScreenTransformAnimation.AngleBetween(OutputTransform.Rotate90, OutputTransform.Normal), 6);
        Assert.Equal(180, ScreenTransformAnimation.AngleBetween(OutputTransform.Normal, OutputTransform.Rotate180), 6);
        Assert.Equal(90, ScreenTransformAnimation.AngleBetween(OutputTransform.Rotate270, OutputTransform.Normal), 6);
        Assert.Equal(0, ScreenTransformAnimation.AngleBetween(OutputTransform.Normal, OutputTransform.Flipped), 6);
    }

    [Fact]
    public void A_zero_duration_does_not_run_either_crossfade()
    {
        using var host = new CompositorTestHost();
        using var blend = new BlendChangesStage(host.Renderer);
        using var transform = new ScreenTransformAnimation(host.Renderer);
        var buffer = new MemoryBuffer(8, 8, DrmFormat.Xrgb8888);
        Assert.False(blend.Begin(buffer, Tick(0), new AnimationDuration(400).WithFactor(0)));
        Assert.False(transform.Begin(
            buffer, OutputTransform.Normal, OutputTransform.Rotate90, Tick(0),
            new AnimationDuration(250).WithFactor(0)));
        buffer.Destroy();
    }
}

public sealed class ShakeCursorTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void A_straight_line_is_not_a_shake()
    {
        var detector = new Basin.Seat.ShakeDetector();
        var shook = false;
        for (var i = 0; i < 40; i++)
        {
            shook |= detector.Update(i * 20, 0, i * 10_000_000);
        }

        Assert.False(shook);
    }

    [Fact]
    public void Sweeping_back_and_forth_is_a_shake()
    {
        var detector = new Basin.Seat.ShakeDetector();
        var shook = false;
        var nanos = 0L;
        for (var sweep = 0; sweep < 8 && !shook; sweep++)
        {
            var forward = sweep % 2 == 0;
            for (var step = 0; step <= 10; step++)
            {
                var x = forward ? step * 30 : 300 - (step * 30);
                nanos += 5_000_000;
                shook |= detector.Update(x, 200, nanos);
            }
        }

        Assert.True(shook, "eight sweeps over three hundred pixels should read as a shake");
    }

    [Fact]
    public void A_shake_inflates_the_cursor_and_it_settles_back()
    {
        var effect = new ShakeCursorEffect();
        Assert.Equal(1.0, effect.Magnification, 6);
        Assert.False(effect.IsActive);

        effect.Shake(Tick(0));
        Assert.Equal(3.0, effect.TargetMagnification, 6);
        effect.Step(Tick(100));
        Assert.True(effect.Magnification > 1.0 && effect.Magnification < 3.0, $"mid ramp {effect.Magnification}");

        effect.Step(Tick(220));
        effect.Step(Tick(240));
        Assert.Equal(3.0, effect.Magnification, 6);

        effect.Step(Tick(2300));
        effect.Step(Tick(2600));
        effect.Step(Tick(2620));
        Assert.Equal(1.0, effect.Magnification, 6);
        Assert.False(effect.IsActive);
    }

    [Fact]
    public void A_second_shake_while_inflated_over_magnifies()
    {
        var effect = new ShakeCursorEffect();
        effect.Shake(Tick(0));
        effect.Step(Tick(220));
        effect.Step(Tick(240));
        Assert.Equal(3.0, effect.Magnification, 6);

        effect.Shake(Tick(300));
        Assert.Equal(4.0, effect.TargetMagnification, 6);
    }
}

public sealed class DimAndHighlightTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void The_dim_strength_is_clamped_to_kwins_range()
    {
        Assert.Equal(0.25, new DimInactiveEffect(null).Strength, 6);
        Assert.Equal(0.1, new DimInactiveEffect(null, new DimInactiveOptions { Strength = 0 }).Strength, 6);
        Assert.Equal(0.9, new DimInactiveEffect(null, new DimInactiveOptions { Strength = 200 }).Strength, 6);
    }

    [Fact]
    public void The_dim_factor_fades_between_none_and_full()
    {
        var effect = new DimInactiveEffect(null);
        Assert.Equal(1.0, effect.Dim, 6);

        effect.FadeTo(1.0, Tick(0), new AnimationDuration(160));
        Assert.True(effect.IsAnimating);
        effect.Step(Tick(80));
        Assert.True(effect.Dim < 1.0 && effect.Dim > 0.75, $"halfway dim {effect.Dim}");

        effect.Step(Tick(180));
        effect.Step(Tick(200));
        Assert.Equal(0.75, effect.Dim, 6);
        Assert.False(effect.IsAnimating);
    }

    [Fact]
    public void A_zero_duration_dims_at_once_rather_than_refusing()
    {
        var effect = new DimInactiveEffect(null);
        effect.FadeTo(1.0, Tick(0), new AnimationDuration(160).WithFactor(0));
        Assert.Equal(0.75, effect.Dim, 6);
        Assert.False(effect.IsAnimating);
    }

    [Fact]
    public void Highlighting_ghosts_the_others_and_heals_the_stack()
    {
        using var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        _ = new SceneRect(tree, 100, 100, new RenderColor(1f, 1f, 1f, 1f));
        var stack = new TransformStack(tree);

        var effect = new HighlightWindowEffect(0.15);
        effect.Highlight(stack, highlighted: false, Tick(0), new AnimationDuration(150));
        var node = stack.Get("highlight");
        Assert.NotNull(node);

        effect.Step(Tick(75));
        Assert.True(node!.Alpha < 1f && node.Alpha > 0.15f, $"midway {node.Alpha}");

        effect.Step(Tick(170));
        effect.Step(Tick(190));
        Assert.Equal(0.15f, node.Alpha, 3);
        Assert.True(effect.IsActive);

        effect.Highlight(stack, highlighted: true, Tick(200), new AnimationDuration(150));
        effect.Step(Tick(370));
        effect.Step(Tick(390));
        Assert.Null(stack.Get("highlight"));
        Assert.False(effect.IsActive);
    }
}

public sealed class MotionEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (CompositorTestHost Host, TransformStack Stack) Window()
    {
        var host = new CompositorTestHost();
        var tree = new SceneTree(host.Scene.Root);
        _ = new SceneRect(tree, 200, 150, new RenderColor(0.5f, 0.5f, 0.5f, 1f));
        return (host, new TransformStack(tree));
    }

    [Fact]
    public void The_window_motion_reaches_its_target_and_the_stop_test_ends_it()
    {
        var motion = new WindowMotion(0, 0.12, 2.5);
        motion.SetTarget(400);
        var peak = 0.0;
        for (var frame = 0; frame < 400 && !motion.IsSettled(); frame++)
        {
            motion.Calculate(16);
            peak = Math.Max(peak, motion.Value);
        }

        Assert.True(motion.IsSettled(), $"settled at {motion.Value}");
        Assert.True(peak > 400 && peak < 410, $"KWin's motion overshoots a little and no more, peaked at {peak}");
        motion.Finish();
        Assert.Equal(400, motion.Value, 6);
    }

    [Fact]
    public void The_spring_motion_settles_on_its_anchor()
    {
        var spring = new SpringMotion(900.0, 1.0) { Anchor = 0 };
        spring.SetPosition(300);
        Assert.True(spring.IsMoving);

        for (var frame = 0; frame < 600 && spring.IsMoving; frame++)
        {
            spring.Advance(16);
        }

        Assert.False(spring.IsMoving);
        Assert.True(Math.Abs(spring.Position) <= 1.0, $"came to rest at {spring.Position}");
    }

    [Fact]
    public void A_slide_back_moves_the_window_and_heals_the_stack()
    {
        var (host, stack) = Window();
        var effect = new SlideBackEffect();
        effect.Move(stack, 0, 0, -200, 0, durationFactor: 1.0);
        var node = stack.Get("slideback");
        Assert.NotNull(node);

        effect.Step(Tick(0));
        effect.Step(Tick(16));
        var (midX, _) = node!.Matrix.Map(0, 0);
        Assert.True(midX < 0 && midX > -200, $"midway {midX}");

        for (var frame = 2; frame < 400 && effect.IsActive; frame++)
        {
            effect.Step(Tick(frame * 16));
        }

        Assert.False(effect.IsActive);
        Assert.Null(stack.Get("slideback"));
        host.Dispose();
    }

    [Fact]
    public void A_zero_duration_factor_puts_the_window_straight_where_it_belongs()
    {
        var (host, stack) = Window();
        var effect = new SlideBackEffect();
        effect.Move(stack, 0, 0, -200, 0, durationFactor: 0);
        var node = stack.Get("slideback");
        Assert.NotNull(node);
        var (x, _) = node!.Matrix.Map(0, 0);
        Assert.Equal(-200, x, 6);
        host.Dispose();
    }

    [Fact]
    public void A_notification_slides_in_on_its_spring_and_lands()
    {
        var (host, stack) = Window();
        var effect = new SlidingNotificationsEffect();
        Assert.True(effect.Slide(stack, 400, 0, 0, 0, durationFactor: 1.0, removeWhenSettled: true));
        var node = stack.Get("notification-slide");
        Assert.NotNull(node);
        var (start, _) = node!.Matrix.Map(0, 0);
        Assert.Equal(400, start, 6);

        effect.Step(Tick(0));
        for (var frame = 1; frame < 600 && effect.IsActive; frame++)
        {
            effect.Step(Tick(frame * 16));
        }

        Assert.False(effect.IsActive);
        Assert.Null(stack.Get("notification-slide"));
        host.Dispose();
    }

    [Fact]
    public void A_zero_duration_factor_lands_the_notification_at_once()
    {
        var (host, stack) = Window();
        var effect = new SlidingNotificationsEffect();
        Assert.False(effect.Slide(stack, 400, 0, 0, 0, durationFactor: 0, removeWhenSettled: false));
        Assert.False(effect.IsActive);
        var node = stack.Get("notification-slide");
        Assert.NotNull(node);
        var (x, _) = node!.Matrix.Map(0, 0);
        Assert.Equal(0, x, 6);
        host.Dispose();
    }
}

public sealed class VisualizerEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void A_click_draws_its_rings_and_expires()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var effect = new MouseClickEffect();
        effect.Attach(overlay);

        effect.Click(100, 100, 0x110, Tick(0));
        Assert.True(effect.IsActive);
        var bounds = new Box(0, 0, 200, 200);
        Assert.Equal(2 * FeedbackShapes.RingVertexCount(48), effect.VertexCount(bounds));

        var vertices = new MeshVertex[effect.VertexCount(bounds)];
        effect.Step(Tick(150));
        effect.WriteVertices(bounds, vertices);
        Assert.Contains(vertices, v => v.Color.A > 0);

        Assert.False(effect.Step(Tick(1200)));
        Assert.False(effect.IsActive);
        effect.Detach(overlay);
    }

    [Fact]
    public void A_touch_contact_holds_its_ring_until_it_lifts()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var effect = new TouchPointsEffect();
        effect.Attach(overlay);

        effect.Down(1, 50, 60, Tick(0));
        Assert.True(effect.Step(Tick(1000)), "a held contact never expires");

        effect.Up(1, Tick(1000));
        Assert.True(effect.Step(Tick(1100)));
        Assert.False(effect.Step(Tick(1400)));
        effect.Detach(overlay);
    }

    [Fact]
    public void A_freehand_mark_keeps_its_segments_and_undo_drops_the_last()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var effect = new MouseMarkEffect();
        effect.Attach(overlay);

        effect.BeginFreehand(10, 10);
        effect.Extend(20, 20);
        effect.Extend(30, 10);
        Assert.True(effect.IsDrawing);
        var bounds = new Box(0, 0, 100, 100);
        Assert.Equal(2 * 6, effect.VertexCount(bounds));

        effect.EndFreehand();
        Assert.False(effect.IsDrawing);
        Assert.Equal(2 * 6, effect.VertexCount(bounds));

        effect.BeginArrow(0, 0);
        effect.EndArrow(80, 0);
        Assert.Equal((2 + 4) * 6, effect.VertexCount(bounds));

        effect.UndoLast();
        Assert.Equal(2 * 6, effect.VertexCount(bounds));
        effect.Clear();
        Assert.False(effect.IsActive);
        effect.Detach(overlay);
    }

    [Fact]
    public void The_tracked_rings_turn_only_while_held()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var effect = new TrackMouseEffect();
        effect.Attach(overlay);

        Assert.False(effect.Step(Tick(1000)));
        Assert.Equal(0, effect.VertexCount(new Box(0, 0, 100, 100)));

        effect.SetCursor(60, 60);
        effect.SetHeld(true);
        Assert.True(effect.Step(Tick(1000)));
        var first = effect.Angle;
        Assert.True(effect.Step(Tick(1500)));
        Assert.NotEqual(first, effect.Angle);
        Assert.Equal(2 * FeedbackShapes.RingVertexCount(64), effect.VertexCount(new Box(0, 0, 200, 200)));

        effect.SetHeld(false);
        Assert.False(effect.Step(Tick(2000)));
        effect.Detach(overlay);
    }
}

public sealed class BellAndStartupTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void The_bell_flashes_for_its_pause_and_refuses_to_restart_inside_it()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var bell = new SystemBellEffect();
        bell.Attach(overlay);

        Assert.True(bell.Flash(new Box(0, 0, 100, 80), Tick(0)));
        Assert.True(bell.IsActive);
        Assert.False(bell.Flash(new Box(0, 0, 100, 80), Tick(100)), "a bell inside its own pause does not restart");

        Assert.True(bell.Step(Tick(300)));
        Assert.False(bell.Step(Tick(600)));
        Assert.False(bell.IsActive);

        Assert.True(bell.Flash(new Box(0, 0, 100, 80), Tick(700)));
        bell.Detach(overlay);
    }

    [Fact]
    public void The_bell_pause_never_goes_below_the_photosensitivity_floor()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var bell = new SystemBellEffect { PauseMillis = 10 };
        bell.Attach(overlay);

        Assert.True(bell.Flash(new Box(0, 0, 100, 80), Tick(0)));
        Assert.True(bell.Step(Tick(100)), "a ten millisecond pause is held at the floor");
        Assert.False(bell.Step(Tick(250)));
        bell.Detach(overlay);
    }

    [Fact]
    public void The_bounce_squeezes_and_lifts_and_the_timeout_ends_it()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var feedback = new StartupFeedbackEffect { Kind = StartupFeedbackKind.Bouncing };
        feedback.Attach(overlay);
        feedback.SetCursor(100, 100);
        feedback.Start(Tick(0));

        Assert.True(feedback.Step(Tick(125)));
        var lift = feedback.BounceOffset;
        Assert.True(feedback.Step(Tick(250)));
        Assert.NotEqual(lift, feedback.BounceOffset);
        Assert.True(Math.Abs(feedback.Squeeze) <= 12.0001, $"the squeeze stays inside KWin's range, got {feedback.Squeeze}");

        Assert.False(feedback.Step(Tick(6000)));
        Assert.False(feedback.IsActive);
        feedback.Detach(overlay);
    }

    [Fact]
    public void The_blink_walks_five_frames_and_passive_draws_nothing()
    {
        using var host = new CompositorTestHost();
        var layers = new SceneLayers(host.Scene.Root);
        using var overlay = new FeedbackOverlay(layers.Feedback);
        var feedback = new StartupFeedbackEffect { Kind = StartupFeedbackKind.Blinking };
        feedback.Attach(overlay);
        feedback.Start(Tick(0));

        var seen = new HashSet<int>();
        for (var frame = 1; frame <= 30; frame++)
        {
            feedback.Step(Tick(frame * 20));
            seen.Add(feedback.Frame);
        }

        Assert.True(seen.Count >= 4, $"walked {seen.Count} of the five blink frames");

        feedback.Kind = StartupFeedbackKind.Passive;
        Assert.False(feedback.IsActive);
        Assert.Equal(0, feedback.VertexCount(new Box(0, 0, 200, 200)));
        feedback.Detach(overlay);
    }
}

public sealed class KwinEffectGoldenTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (SceneTree Window, TransformStack Stack) Window(CompositorTestHost host)
    {
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(24, 18);
        _ = new SceneRect(window, 80, 60, new RenderColor(0.25f, 0.4f, 0.75f, 1f));
        var content = new SceneRect(window, 64, 44, new RenderColor(0.9f, 0.65f, 0.2f, 1f));
        content.SetPosition(8, 8);
        return (window, new TransformStack(window));
    }

    [Theory]
    [InlineData(0, "glide-open-0")]
    [InlineData(80, "glide-open-80")]
    [InlineData(160, "glide-open-160")]
    public void Glide_frames_are_deterministic(int millis, string golden)
    {
        using var host = new CompositorTestHost();
        var (_, stack) = Window(host);
        var glide = new GlideEffect();
        glide.Begin(stack, hiding: false, Tick(0), new AnimationDuration(160));
        glide.Step(stack, Tick(millis));
        host.RenderFrame();
        Golden.AssertMatches(host, golden);
    }

    [Theory]
    [InlineData(0, "sheet-open-0")]
    [InlineData(150, "sheet-open-150")]
    [InlineData(300, "sheet-open-300")]
    public void Sheet_frames_are_deterministic(int millis, string golden)
    {
        using var host = new CompositorTestHost();
        var (_, stack) = Window(host);
        var sheet = new SheetEffect();
        sheet.Begin(stack, hiding: false, parentDrop: 40, Tick(0), new AnimationDuration(300));
        sheet.Step(stack, Tick(millis));
        host.RenderFrame();
        Golden.AssertMatches(host, golden);
    }

    [Theory]
    [InlineData(MinimizeEdge.Bottom, 60, "magiclamp-bottom")]
    [InlineData(MinimizeEdge.Top, 60, "magiclamp-top")]
    [InlineData(MinimizeEdge.Left, 60, "magiclamp-left")]
    [InlineData(MinimizeEdge.Right, 60, "magiclamp-right")]
    [InlineData(MinimizeEdge.Bottom, 125, "magiclamp-bottom-late")]
    public void Magic_lamp_frames_are_deterministic(MinimizeEdge edge, int millis, string golden)
    {
        using var host = new CompositorTestHost();
        var (_, stack) = Window(host);
        var window = new Box(24, 18, 80, 60);
        var icon = edge switch
        {
            MinimizeEdge.Bottom => new Box(50, 110, 20, 6),
            MinimizeEdge.Top => new Box(50, 0, 20, 6),
            MinimizeEdge.Left => new Box(0, 40, 6, 20),
            _ => new Box(150, 40, 6, 20),
        };

        var lamp = new MagicLampEffect();
        lamp.Begin(stack, window, icon, edge, restoring: false, Tick(0), new AnimationDuration(250));
        lamp.Step(Tick(millis));
        host.RenderFrame();
        Golden.AssertMatches(host, golden);
    }

    [Theory]
    [InlineData(300, "fallapart-300")]
    [InlineData(600, "fallapart-600")]
    [InlineData(900, "fallapart-900")]
    public void Fall_apart_frames_are_deterministic(int millis, string golden)
    {
        using var host = new CompositorTestHost();
        var (_, stack) = Window(host);
        var effect = new FallApartEffect(20);
        effect.Begin(stack, Tick(0), new AnimationDuration(1000));
        effect.Step(Tick(millis));
        host.RenderFrame();
        Golden.AssertMatches(host, golden);
    }

    [Theory]
    [InlineData(0, "squash-0")]
    [InlineData(125, "squash-125")]
    public void Squash_frames_are_deterministic(int millis, string golden)
    {
        using var host = new CompositorTestHost();
        var (_, stack) = Window(host);
        var squash = new SquashEffect();
        squash.Begin(stack, new Box(24, 18, 80, 60), new Box(60, 110, 20, 6), restoring: false, Tick(0), new AnimationDuration(250));
        squash.Step(stack, Tick(millis));
        host.RenderFrame();
        Golden.AssertMatches(host, golden);
    }
}
