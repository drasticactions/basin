using Basin.Capabilities;
using Basin.Physics;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class PhysicsWorldTests
{
    private const long Second = 1_000_000_000;

    public static TheoryData<string> Engines => new() { "box2d", "bepu" };

    private static PhysicsEngine EngineOf(string name) => name switch
    {
        "box2d" => Box2DEngine.Instance,
        "bepu" => BepuEngine.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("box2d", "bepu")]
    [InlineData("bepu", "bepu")]
    public void A_moved_body_keeps_its_listed_state_in_the_target_world(string from, string to)
    {
        using var source = new PhysicsWorld(EngineOf(from));
        using var target = new PhysicsWorld(EngineOf(to));
        var body = source.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 120, 80, Sliding, tag: 42);
        body.CollidesWith = PhysicsCategory.Windows | PhysicsCategory.Edges;
        body.AngularLocked = true;
        body.Velocity = (250, -40);

        var moved = source.MoveTo(body, target, 300, 200, velocityScale: 1);

        Assert.False(body.IsValid);
        Assert.True(moved.IsValid);
        Assert.Same(target, moved.World);
        Assert.Equal(PhysicsBodyKind.Dynamic, moved.Kind);
        Assert.Equal(42ul, moved.Tag);
        Assert.Equal(120, moved.Width, 3);
        Assert.Equal(80, moved.Height, 3);
        Assert.Equal(PhysicsCategory.Windows | PhysicsCategory.Edges, moved.CollidesWith);
        Assert.True(moved.AngularLocked);
        Assert.Equal(250, moved.Velocity.X, 1);
        Assert.Equal(-40, moved.Velocity.Y, 1);
    }

    private static PhysicsWorld Boxed(string engine, int width = 2000, int height = 1200)
    {
        var world = new PhysicsWorld(EngineOf(engine));
        var walls = new PhysicsWalls(world);
        walls.Rebuild([new Box(0, 0, width, height)]);
        return world;
    }

    private static long Run(PhysicsWorld world, double hz, long start = Second, int limit = 20_000)
    {
        var interval = (long)(Second / hz);
        var now = start;
        for (var i = 0; i < limit && world.Step(new FrameTick(now, interval)); i++)
        {
            now += interval;
        }

        return now;
    }

    private static PhysicsMaterial Sliding => new(Density: 1, Friction: 0.3, Restitution: 0.1, GravityScale: 1, LinearDamping: 2);

    [Theory]
    [MemberData(nameof(Engines))]
    public void The_same_inputs_give_bit_identical_poses(string engine)
    {
        long[] Once()
        {
            using var world = Boxed(engine);
            var a = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 300, 200, Sliding);
            var b = world.CreateBody(PhysicsBodyKind.Dynamic, 900, 150, 250, 250, Sliding);
            a.Velocity = (2500, 300);
            a.AngularVelocity = 1.5;
            var now = Second;
            var poses = new List<long>();
            for (var i = 0; i < 240; i++)
            {
                world.Step(new FrameTick(now += 6_060_606, 6_060_606));
                poses.Add(BitConverter.DoubleToInt64Bits(a.Pose.X + (a.Pose.Y * 3) + b.Pose.X + b.Pose.Angle));
            }

            return [.. poses];
        }

        Assert.Equal(Once(), Once());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_throw_rests_at_the_same_place_at_any_refresh_rate(string engine)
    {
        PhysicsPose Throw(double hz)
        {
            using var world = Boxed(engine);
            var body = world.CreateBody(PhysicsBodyKind.Dynamic, 200, 300, 300, 200, Sliding);
            body.AngularLocked = true;
            body.Velocity = (1800, -400);
            Run(world, hz);
            return body.Pose;
        }

        var sixty = Throw(60);
        var fast = Throw(144);
        var arc = Throw(165);

        Assert.InRange(Math.Abs(sixty.X - fast.X), 0, 1);
        Assert.InRange(Math.Abs(sixty.Y - fast.Y), 0, 1);
        Assert.InRange(Math.Abs(sixty.X - arc.X), 0, 1);
        Assert.InRange(Math.Abs(sixty.Y - arc.Y), 0, 1);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_settled_world_is_idle_integral_and_upright(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 200, 300, 300, 200, Sliding);
        body.AngularLocked = true;
        body.Velocity = (1200, 700);
        var now = Run(world, 60);

        Assert.False(world.IsAwake);
        Assert.False(world.Step(new FrameTick(now + 16_666_667, 16_666_667)));
        Assert.True(world.Moved.IsEmpty);
        var pose = body.Pose;
        Assert.Equal(Math.Round(pose.X), pose.X);
        Assert.Equal(Math.Round(pose.Y), pose.Y);
        Assert.True(pose.Rotation.IsIdentity);
        Assert.NotEqual(200, pose.X);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Moved_reports_each_body_whose_pose_changed(string engine)
    {
        using var world = Boxed(engine);
        var still = world.CreateBody(PhysicsBodyKind.Dynamic, 1500, 800, 100, 100, Sliding);
        var thrown = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 100, 100, Sliding);
        world.Step(new FrameTick(Second, 16_666_667));
        thrown.Velocity = (900, 0);
        world.Step(new FrameTick(Second + 16_666_667, 16_666_667));
        world.Step(new FrameTick(Second + 33_333_334, 16_666_667));

        Assert.Contains(thrown, world.Moved.ToArray());
        Assert.DoesNotContain(still, world.Moved.ToArray());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Ticks_that_do_not_advance_do_not_step(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 100, 100, Sliding);
        body.Velocity = (900, 0);
        world.Step(new FrameTick(Second, 16_666_667));
        world.Step(new FrameTick(Second + 33_333_334, 16_666_667));
        var pose = body.Pose;

        Assert.True(world.Step(new FrameTick(Second + 33_333_334, 16_666_667)));
        Assert.True(world.Moved.IsEmpty);
        Assert.Equal(pose, body.Pose);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Bodies_created_asleep_overlap_until_disturbed(string engine)
    {
        using var world = Boxed(engine);
        var first = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 300, 200, Sliding, awake: false);
        var second = world.CreateBody(PhysicsBodyKind.Dynamic, 130, 130, 300, 200, Sliding, awake: false);

        Assert.False(world.IsAwake);
        Assert.False(world.Step(new FrameTick(Second, 16_666_667)));
        second.Teleport(140, 140);
        Assert.False(world.IsAwake);
        Assert.Equal(100, first.Pose.X);

        second.Velocity = (1, 0);
        Run(world, 60);
        Assert.True(first.Pose.X != 100 || first.Pose.Y != 100, "the disturbed pair separates");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Locking_the_rotation_holds_the_current_angle(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 300, 300, 200, 100, Sliding);
        body.Teleport(300, 300, 0.4);
        body.AngularLocked = true;
        body.Velocity = (800, 0);
        body.AngularVelocity = 5;
        Run(world, 60);

        Assert.Equal(0.4, body.Pose.Angle, 4);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Walls_keep_a_throw_inside(string engine)
    {
        using var world = Boxed(engine, 800, 600);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 300, 250, 100, 100, Sliding);
        body.AngularLocked = true;
        body.Velocity = (6000, 3000);
        Run(world, 60);

        var pose = body.Pose;
        Assert.InRange(pose.X, 0, 700);
        Assert.InRange(pose.Y, 0, 500);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Two_outputs_of_different_heights_fence_the_dead_zone(string engine)
    {
        using var world = new PhysicsWorld(EngineOf(engine));
        var walls = new PhysicsWalls(world);
        walls.Rebuild([new Box(0, 0, 1000, 800), new Box(1000, 0, 600, 400)]);

        Assert.Equal(new Box(0, 0, 1600, 800), walls.Bounds);
        Assert.Equal([new Box(1000, 400, 600, 400)], walls.Fences);

        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 1100, 100, 100, 100, Sliding);
        body.AngularLocked = true;
        body.Velocity = (0, 3000);
        Run(world, 60);
        Assert.InRange(body.Pose.Y + 100, 0, 400.5);

        walls.Rebuild([new Box(0, 0, 1000, 800), new Box(1000, 0, 600, 800)]);
        Assert.Empty(walls.Fences);
        body.Velocity = (0, 3000);
        Run(world, 60);
        Assert.True(body.Pose.Y + 100 > 400.5, $"fell to {body.Pose.Y}");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Offset_outputs_fence_both_gaps(string engine)
    {
        using var world = new PhysicsWorld(EngineOf(engine));
        var walls = new PhysicsWalls(world);
        walls.Rebuild([new Box(0, 200, 1000, 600), new Box(1000, 0, 800, 600)]);

        Assert.Equal(new Box(0, 0, 1800, 800), walls.Bounds);
        Assert.Contains(new Box(0, 0, 1000, 200), walls.Fences);
        Assert.Contains(new Box(1000, 600, 800, 200), walls.Fences);
        Assert.Equal(2, walls.Fences.Count);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_spinning_throw_rests_at_exactly_zero_degrees(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 600, 400, 300, 200, Sliding);
        body.Upright = true;
        body.Velocity = (500, 200);
        body.AngularVelocity = 6;
        var now = Second;
        var tilted = false;
        for (var i = 0; i < 20_000 && world.Step(new FrameTick(now += 6_060_606, 6_060_606)); i++)
        {
            tilted |= Math.Abs(body.Pose.Angle) > 0.1;
        }

        Assert.True(tilted);
        Assert.False(world.IsAwake);
        Assert.Equal(0, body.Pose.Angle);
        Assert.True(body.Pose.Rotation.IsIdentity);
        Assert.Equal(PhysicsBodyKind.Dynamic, body.Kind);
        Assert.Equal(Math.Round(body.Pose.X), body.Pose.X);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Without_upright_a_tilted_body_stays_tilted(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 600, 400, 300, 200, Sliding);
        body.Velocity = (100, 0);
        body.AngularVelocity = 3;
        Run(world, 60);

        Assert.False(world.IsAwake);
        Assert.NotEqual(0, body.Pose.Angle);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_collision_is_an_impact_with_its_speed(string engine)
    {
        using var world = Boxed(engine);
        var moving = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 400, 200, 200, Sliding);
        var target = world.CreateBody(PhysicsBodyKind.Dynamic, 800, 400, 200, 200, Sliding);
        moving.AngularLocked = true;
        target.AngularLocked = true;
        moving.Velocity = (3000, 0);
        var now = Second;
        PhysicsImpact? impact = null;
        for (var i = 0; i < 200 && impact is null && world.Step(new FrameTick(now += 16_666_667, 16_666_667)); i++)
        {
            foreach (var hit in world.Impacts)
            {
                impact ??= hit;
            }
        }

        Assert.NotNull(impact);
        var pair = new[] { impact!.Value.A, impact.Value.B };
        Assert.Contains(moving, pair);
        Assert.Contains(target, pair);
        Assert.True(impact.Value.Speed > world.ImpactThreshold);
        Assert.InRange(impact.Value.X, 280, 820);
    }

    [Theory]
    [InlineData("box2d", 0.0, 0.25, 0.1)]
    [InlineData("box2d", 1.0, 0.88, 0.05)]
    [InlineData("bepu", 0.0, 0.03, 0.03)]
    [InlineData("bepu", 1.0, 0.46, 0.05)]
    public void Restitution_sets_how_much_speed_a_wall_bounce_keeps(string engine, double restitution, double kept, double tolerance)
    {
        using var world = Boxed(engine, 4000, 1200);
        var material = new PhysicsMaterial(Density: 1, Friction: 0, Restitution: restitution, GravityScale: 1, LinearDamping: 0);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 1500, 500, 100, 100, material);
        body.AngularLocked = true;
        body.Velocity = (3000, 0);
        var now = Second;
        for (var i = 0; i < 600 && world.IsAwake && (body.Velocity.X >= 0 || body.Current.X > 2900); i++)
        {
            world.Step(new FrameTick(now += 16_666_667, 16_666_667));
        }

        Assert.InRange(-body.Velocity.X / 3000, kept - tolerance, kept + tolerance);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_drag_follows_the_pointer_exactly_and_the_release_throws(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 200, 100, Sliding);
        body.AngularLocked = true;
        world.BeginDrag(body, 150, 120);
        var now = Second;
        for (var i = 1; i <= 10; i++)
        {
            world.Drag(body, 150 + (i * 20), 120 + (i * 5));
            world.Step(new FrameTick(now += 16_666_667, 16_666_667));
            Assert.Equal(100 + (i * 20), body.Pose.X, 6);
            Assert.Equal(100 + (i * 5), body.Pose.Y, 6);
        }

        Assert.Equal(PhysicsBodyKind.Kinematic, body.IsDragging ? PhysicsBodyKind.Kinematic : body.Kind);
        world.EndDrag(body, 1200, 300);
        Assert.Equal(300, body.Pose.X, 3);
        Assert.Equal(PhysicsBodyKind.Dynamic, body.Kind);
        world.Step(new FrameTick(now += 16_666_667, 16_666_667));
        world.Step(new FrameTick(now += 16_666_667, 16_666_667));
        Assert.True(body.Pose.X > 300);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_drag_jump_past_the_teleport_distance_skips_the_sweep(string engine)
    {
        using var world = Boxed(engine, 4000, 1200);
        world.TeleportDistance = 1000;
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 200, 100, Sliding);
        var between = world.CreateBody(PhysicsBodyKind.Dynamic, 1400, 100, 200, 200, Sliding);
        world.BeginDrag(body, 150, 150);
        world.Drag(body, 2650, 150);
        world.Step(new FrameTick(Second, 16_666_667));
        world.Step(new FrameTick(Second + 16_666_667, 16_666_667));

        Assert.Equal(2600, body.Current.X, 3);
        Assert.Equal(1400, between.Pose.X, 3);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Moving_to_another_world_scales_the_velocity(string engine)
    {
        using var from = Boxed(engine);
        using var to = Boxed(engine);
        var body = from.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 200, 100, Sliding, tag: 42);
        body.Velocity = (1000, 0);

        var moved = from.MoveTo(body, to, 300, 300, 0.5);

        Assert.False(body.IsValid);
        Assert.True(moved.IsValid);
        Assert.Equal(42UL, moved.Tag);
        Assert.Equal(500, moved.Velocity.X, 1);
        Assert.Equal(300, moved.Pose.X, 3);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_frozen_world_does_not_step(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 200, 100, Sliding);
        body.Velocity = (1000, 0);
        world.Frozen = true;

        Assert.False(world.Step(new FrameTick(Second, 16_666_667)));
        Assert.False(world.Step(new FrameTick(Second + Second, 16_666_667)));
        Assert.Equal(100, body.Pose.X);
        Assert.False(world.IsAwake);

        world.Frozen = false;
        Assert.True(world.IsAwake);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Gravity_pulls_to_the_floor(string engine)
    {
        using var world = Boxed(engine, 800, 600);
        world.Gravity = (0, 2000);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 300, 50, 100, 100, Sliding);
        body.AngularLocked = true;
        Run(world, 60);

        Assert.InRange(body.Pose.Y, 498, 500.5);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_stack_under_gravity_settles_level_and_asleep(string engine)
    {
        using var world = Boxed(engine, 800, 600);
        world.Gravity = (0, 2000);
        var bottom = world.CreateBody(PhysicsBodyKind.Dynamic, 300, 400, 300, 150, Sliding);
        var middle = world.CreateBody(PhysicsBodyKind.Dynamic, 320, 200, 250, 120, Sliding);
        var top = world.CreateBody(PhysicsBodyKind.Dynamic, 340, 20, 200, 100, Sliding);
        var now = Run(world, 60);

        Assert.False(world.IsAwake, $"still awake at {(now - Second) / 1e9:F1}s");
        Assert.InRange(bottom.Pose.Y, 448, 451.5);
        Assert.InRange(middle.Pose.Y, 328, 331.5);
        Assert.InRange(top.Pose.Y, 228, 231.5);
        Assert.InRange(Math.Abs(top.Pose.Angle), 0, 0.01);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_spinning_throw_from_a_resting_pile_keeps_moving(string engine)
    {
        using var world = Boxed(engine);
        var left = world.CreateBody(PhysicsBodyKind.Dynamic, 200, 500, 256, 256, Sliding);
        var thrown = world.CreateBody(PhysicsBodyKind.Dynamic, 456, 500, 256, 256, Sliding);
        world.CreateBody(PhysicsBodyKind.Dynamic, 1000, 500, 256, 256, Sliding);
        world.CreateBody(PhysicsBodyKind.Dynamic, 1400, 500, 256, 256, Sliding);
        Run(world, 60);

        thrown.Velocity = (-1500, 500);
        thrown.AngularVelocity = 3;
        var now = Run(world, 60, limit: 20);

        Assert.True(world.IsAwake, "the throw slept within 20 frames");
        Assert.True(thrown.Pose.Y > 540, $"the throw stopped at {thrown.Pose.Y}");
        Assert.True(left.Pose.X < 200, $"the pushed window stayed at {left.Pose.X}");
        _ = now;
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_throw_does_not_move_a_static_window(string engine)
    {
        using var world = Boxed(engine);
        var pinned = world.CreateBody(PhysicsBodyKind.Static, 800, 400, 300, 300, Sliding);
        var thrown = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 450, 200, 200, Sliding);
        thrown.Velocity = (4000, 0);
        Run(world, 60);

        Assert.Equal(800, pinned.Pose.X);
        Assert.Equal(400, pinned.Pose.Y);
        Assert.Equal(0, pinned.Pose.Angle);
        Assert.True(thrown.Pose.X + 200 <= 801, $"passed through to {thrown.Pose.X}");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_resize_moves_the_body_size_and_mass(string engine)
    {
        using var world = Boxed(engine);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 200, 100, Sliding);
        var mass = world.MassOf(body);
        body.Resize(400, 200);

        Assert.Equal(400, body.Width);
        Assert.Equal(200, body.Height);
        Assert.Equal(mass * 4, world.MassOf(body), 3);
        Assert.Equal(100, body.Pose.X, 3);
        Assert.Equal(100, body.Pose.Y, 3);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_bound_window_tracks_its_committed_size_without_a_consumer_call(string engine)
    {
        using var host = new CompositorTestHost();
        var model = new AggregateToplevelModel();
        using var source = new XdgToplevelSource(host.Shell);
        model.Add(source);
        var window = MappedToplevel.Map(host, host.Client, 60, 50);
        var infos = new ToplevelInfo[4];
        Assert.Equal(1, model.Enumerate(infos));
        var id = infos[0].Id;

        using var world = new PhysicsWorld(EngineOf(engine));
        using var windows = new PhysicsWindows(model);
        var body = windows.Bind(world, id, 10, 20, PhysicsBodyKind.Dynamic, Sliding, new PhysicsInsets(2, 20, 2, 2));
        Assert.Equal(64, body.Width);
        Assert.Equal(72, body.Height);
        var mass = world.MassOf(body);

        var bigger = host.Client.CreateBuffer(120, 100, Fill.Solid(120, 100, 0xFF112233));
        window.Surface.Attach(bigger.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 120, 100);
        window.Surface.Commit();
        host.PumpUntil(() => body.Width == 124);

        Assert.Equal(122, body.Height);
        Assert.True(world.MassOf(body) > mass * 3);
        var content = windows.ContentPose(id, body.Pose);
        Assert.Equal(10, content.X, 3);
        Assert.Equal(20, content.Y, 3);

        window.Toplevel.Destroy();
        window.XdgSurface.Destroy();
        window.Surface.Dispose();
        host.PumpUntil(() => !body.IsValid);
        Assert.Equal(0, windows.Count);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Basin_code_around_the_engine_allocates_nothing(string engine)
    {
        using var world = Boxed(engine, 400, 1200);
        var loose = new PhysicsMaterial(Density: 1, Friction: 0, Restitution: 1, GravityScale: 1, LinearDamping: 0);
        var body = world.CreateBody(PhysicsBodyKind.Dynamic, 150, 500, 100, 100, loose);
        body.Velocity = (3000, 400);
        var now = Second;
        for (var i = 0; i < 400; i++)
        {
            now += 6_060_606;
            Basin.Diagnostics.AllocationScope.Begin(warmup: 20, region: engine == "box2d" ? "physics-test-step-box2d" : "physics-test-step-bepu");
            world.Step(new FrameTick(now, 6_060_606));
            foreach (var moved in world.Moved)
            {
                _ = moved.Pose;
            }

            Basin.Diagnostics.AllocationScope.End();
        }
    }

    [Theory]
    [InlineData("box2d", "physics-step-resting")]
    [InlineData("box2d", "physics-step-moving")]
    [InlineData("box2d", "physics-step-contacts")]
    [InlineData("bepu", "physics-step-resting")]
    [InlineData("bepu", "physics-step-moving")]
    [InlineData("bepu", "physics-step-contacts")]
    public void A_step_allocates_what_the_budget_records(string engine, string path)
    {
        Budgets.Require();
        const int Rounds = 100;
        using var world = new PhysicsWorld(EngineOf(engine));
        var walls = new PhysicsWalls(world);
        var loose = new PhysicsMaterial(Density: 1, Friction: 0, Restitution: 1, GravityScale: 1, LinearDamping: 0);
        switch (path)
        {
            case "physics-step-resting":
                walls.Rebuild([new Box(0, 0, 2000, 1200)]);
                world.CreateBody(PhysicsBodyKind.Dynamic, 100, 100, 200, 200, loose);
                world.CreateBody(PhysicsBodyKind.Dynamic, 600, 100, 200, 200, loose);
                break;
            case "physics-step-moving":
                walls.Rebuild([new Box(0, 0, 1_000_000, 1200)]);
                for (var i = 0; i < 4; i++)
                {
                    var body = world.CreateBody(PhysicsBodyKind.Dynamic, 100, 50 + (i * 250), 200, 200, loose);
                    body.AngularLocked = true;
                    body.Velocity = (600, 0);
                }

                break;
            default:
                walls.Rebuild([new Box(0, 0, 400, 1200)]);
                var bouncing = world.CreateBody(PhysicsBodyKind.Dynamic, 150, 500, 100, 100, loose);
                bouncing.AngularLocked = true;
                bouncing.Velocity = (3000, 0);
                break;
        }

        var now = Second;
        var impacts = 0;
        long Measure()
        {
            long total = 0;
            for (var i = 0; i < Rounds; i++)
            {
                now += 16_666_667;
                var tick = new FrameTick(now, 16_666_667);
                var before = GC.GetAllocatedBytesForCurrentThread();
                world.Step(tick);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
                impacts += world.Impacts.Length;
            }

            return total;
        }

        _ = Measure();
        impacts = 0;
        var measured = Measure();
        Assert.Equal(path == "physics-step-contacts", impacts > 0);
        Assert.Equal(path != "physics-step-resting", world.IsAwake);
        Budgets.Check("engine", engine == "box2d" ? path : $"{path}-{engine}", measured);
    }
}
