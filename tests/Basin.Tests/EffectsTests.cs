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
