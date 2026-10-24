using System.Numerics;
using Basin.Physics;
using Xunit;

namespace Basin.Tests;

public sealed class PhysicsWorld3DTests
{
    private const long Second = 1_000_000_000;
    private const long Frame = 16_666_667;
    private static readonly PhysicsMaterial Loose = new(Density: 300, Friction: 0.5, Restitution: 0.1, GravityScale: 1, LinearDamping: 0.05);

    [Fact]
    public void A_dropped_box_comes_to_rest_on_the_floor_and_the_world_idles()
    {
        using var world = Room();
        var box = world.AddBox(new Vector3(0.25f), PhysicsPose3D.At(new Vector3(0, 2, 0)), Loose);
        var now = Second;
        var frames = Run(world, ref now, 600, stopWhenIdle: true);

        Assert.False(world.IsAwake);
        Assert.True(frames < 600, "the world never went idle");
        Assert.InRange(box.Pose.Position.Y, 0.2f, 0.3f);
    }

    [Fact]
    public void A_fast_drop_reports_an_impact_against_the_floor()
    {
        using var world = Room();
        world.ImpactThreshold = 1;
        var box = world.AddBox(new Vector3(0.2f), PhysicsPose3D.At(new Vector3(0, 3, 0)), Loose);
        world.SetVelocity(box, new Vector3(0, -6, 0), Vector3.Zero);
        var now = Second;
        var impacts = 0;
        for (var i = 0; i < 60; i++)
        {
            now += Frame;
            world.Step(new FrameTick(now, Frame));
            foreach (var impact in world.Impacts)
            {
                if (impact.A == box || impact.B == box)
                {
                    impacts++;
                    Assert.True(impact.Speed > 1);
                }
            }
        }

        Assert.True(impacts > 0);
    }

    [Fact]
    public void A_character_stands_walks_along_its_view_and_is_stopped_by_a_wall()
    {
        using var world = Room();
        world.AddStaticBox(new Vector3(0, 1.5f, -3), new Vector3(5, 1.5f, 0.25f));
        var character = world.AddCharacter(0.35f, 1.7f, new Vector3(0, 0.05f, 0));
        var now = Second;
        Run(world, ref now, 60, stopWhenIdle: false);
        Assert.True(character.Supported);
        Assert.InRange(character.Position.Y, 0.8f, 0.9f);

        character.Move(new Vector2(0, 3), -Vector3.UnitZ);
        Run(world, ref now, 30, stopWhenIdle: false);
        Assert.True(character.Position.Z < -0.8f, $"walked only to z={character.Position.Z}");

        Run(world, ref now, 180, stopWhenIdle: false);
        Assert.True(character.Position.Z > -3 + 0.25f + 0.3f, $"walked through the wall to z={character.Position.Z}");

        character.Move(Vector2.Zero, -Vector3.UnitZ);
        character.Jump();
        var peak = 0f;
        for (var i = 0; i < 60; i++)
        {
            now += Frame;
            world.Step(new FrameTick(now, Frame));
            peak = MathF.Max(peak, character.Position.Y);
        }

        Assert.True(peak > 1.2f, $"the jump peaked at {peak}");
        Assert.True(character.Supported);
    }

    [Fact]
    public void A_character_pushes_a_light_prop()
    {
        using var world = Room();
        var barrel = world.AddCylinder(0.3f, 0.4f, PhysicsPose3D.At(new Vector3(0, 0.4f, -1.2f)), new PhysicsMaterial(Density: 30, Friction: 0.3, Restitution: 0, GravityScale: 1, LinearDamping: 0.1));
        var character = world.AddCharacter(0.35f, 1.7f, new Vector3(0, 0.05f, 0));
        var start = barrel.Pose.Position;
        var now = Second;
        character.Move(new Vector2(0, 3), -Vector3.UnitZ);
        Run(world, ref now, 90, stopWhenIdle: false);
        Assert.True(barrel.Pose.Position.Z < start.Z - 0.3f, $"the barrel stayed at z={barrel.Pose.Position.Z}");
    }

    [Fact]
    public void A_dragged_body_follows_the_grab_point_and_a_release_throws_it()
    {
        using var world = Room();
        var slab = world.AddBox(new Vector3(0.4f, 0.3f, 0.01f), PhysicsPose3D.At(new Vector3(0, 1.6f, 0)), new PhysicsMaterial(Density: 400, Friction: 0.4, Restitution: 0.2, GravityScale: 1, LinearDamping: 0.1));
        var now = Second;
        world.BeginDrag(slab, new Vector3(0.1f, 1.7f, 0));
        world.DragTo(slab, new Vector3(1.1f, 1.7f, 0.5f));
        Run(world, ref now, 90, stopWhenIdle: false);
        var grab = world.GrabPoint(slab);
        Assert.True(Vector3.Distance(grab, new Vector3(1.1f, 1.7f, 0.5f)) < 0.1f, $"the grab point is at {grab}");
        Assert.True(world.IsDragging(slab));

        world.EndDrag(slab, new Vector3(0, 0, -4));
        now += Frame;
        world.Step(new FrameTick(now, Frame));
        Assert.True(slab.Velocity.Z < -2);
        Assert.False(world.IsDragging(slab));
    }

    [Fact]
    public void A_dragged_body_turns_to_the_orientation_it_is_given()
    {
        using var world = Room();
        var slab = world.AddBox(new Vector3(0.4f, 0.3f, 0.01f), PhysicsPose3D.At(new Vector3(0, 1.6f, 0)), new PhysicsMaterial(Density: 400, Friction: 0.4, Restitution: 0.2, GravityScale: 1, LinearDamping: 0.1));
        var now = Second;
        world.BeginDrag(slab, new Vector3(0, 1.6f, 0));
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        world.DragTurn(slab, turned);
        Run(world, ref now, 120, stopWhenIdle: false);
        var normal = Vector3.Transform(Vector3.UnitZ, slab.Pose.Orientation);
        Assert.True(Vector3.Dot(normal, Vector3.UnitX) > 0.97f, $"the slab faces {normal}");
        Assert.True(world.IsDragging(slab));

        world.EndDrag(slab, Vector3.Zero);
        world.DragTurn(slab, Quaternion.Identity);
        Assert.False(world.IsDragging(slab));
    }

    [Fact]
    public void A_pose_between_steps_is_interpolated()
    {
        using var world = Room();
        var box = world.AddBox(new Vector3(0.2f), PhysicsPose3D.At(new Vector3(0, 1, 0)), new PhysicsMaterial(Density: 100, Friction: 0, Restitution: 0, GravityScale: 0, LinearDamping: 0));
        world.SetVelocity(box, new Vector3(6, 0, 0), Vector3.Zero);
        var now = Second;
        world.Step(new FrameTick(now, Frame));
        now += Frame + (Frame / 2);
        world.Step(new FrameTick(now, Frame));
        var pose = box.Pose.Position.X;
        var current = box.Current.Position.X;
        Assert.True(pose < current, "the interpolated pose is not behind the stepped one");
    }

    [Theory]
    [InlineData("physics-step-resting-3d")]
    [InlineData("physics-step-moving-3d")]
    [InlineData("physics-step-contacts-3d")]
    public void A_step_allocates_what_the_budget_records(string path)
    {
        Budgets.Require();
        const int Rounds = 100;
        using var world = Room();
        switch (path)
        {
            case "physics-step-resting-3d":
                world.AddBox(new Vector3(0.25f), PhysicsPose3D.At(new Vector3(0, 0.25f, 0)), Loose, awake: false);
                world.AddBox(new Vector3(0.25f), PhysicsPose3D.At(new Vector3(1, 0.25f, 0)), Loose, awake: false);
                break;
            case "physics-step-moving-3d":
                for (var i = 0; i < 4; i++)
                {
                    var body = world.AddBox(new Vector3(0.2f), PhysicsPose3D.At(new Vector3(i, 3, 0)), new PhysicsMaterial(100, 0, 0, 0, 0));
                    world.SetVelocity(body, new Vector3(0.5f, 0, 0), Vector3.Zero);
                }

                break;
            default:
                world.ImpactThreshold = 0.1;
                var bouncing = world.AddBox(new Vector3(0.2f), PhysicsPose3D.At(new Vector3(0, 0.5f, 0)), new PhysicsMaterial(100, 0, 1, 1, 0));
                Bouncer = bouncing;
                world.SetVelocity(bouncing, new Vector3(4, 3, 0), Vector3.Zero);
                world.AddStaticBox(new Vector3(-2, 1, 0), new Vector3(0.1f, 1, 2));
                world.AddStaticBox(new Vector3(2, 1, 0), new Vector3(0.1f, 1, 2));
                world.AddStaticBox(new Vector3(0, 2.5f, 0), new Vector3(3, 0.1f, 3));
                break;
        }

        var now = Second;
        var impacts = 0;
        long Measure()
        {
            long total = 0;
            for (var i = 0; i < Rounds; i++)
            {
                if (path == "physics-step-contacts-3d" && i % 20 == 0)
                {
                    world.SetVelocity(Bouncer!.Value, new Vector3(i % 40 == 0 ? 5 : -5, 2, 0), Vector3.Zero);
                }

                now += Frame;
                var tick = new FrameTick(now, Frame);
                var before = GC.GetAllocatedBytesForCurrentThread();
                world.Step(tick);
                foreach (var moved in world.Moved)
                {
                    _ = moved.Pose;
                }

                total += GC.GetAllocatedBytesForCurrentThread() - before;
                impacts += world.Impacts.Length;
            }

            return total;
        }

        _ = Measure();
        impacts = 0;
        var measured = Measure();
        Assert.Equal(path == "physics-step-contacts-3d", impacts > 0);
        Assert.Equal(path != "physics-step-resting-3d", world.IsAwake);
        Budgets.Check("engine", path, measured);
    }

    [Fact]
    public void A_disposed_world_steps_nothing_and_refuses_changes()
    {
        var world = Room();
        var box = world.AddBox(new Vector3(0.25f), PhysicsPose3D.At(new Vector3(0, 2, 0)), Loose);
        world.Dispose();

        world.StepOnce();
        Assert.Equal(0, world.Moved.Length);
        Assert.False(world.Step(new FrameTick(Second, Frame)));
        Assert.Throws<ObjectDisposedException>(() => world.AddBox(new Vector3(0.25f), PhysicsPose3D.At(Vector3.Zero), Loose));
        Assert.Throws<ObjectDisposedException>(() => world.AddStaticBox(Vector3.Zero, Vector3.One));
        Assert.Throws<ObjectDisposedException>(() => world.Teleport(box, PhysicsPose3D.At(Vector3.Zero)));
        world.Remove(box);
        Assert.False(world.IsDragging(box));
    }

    private static PhysicsBody3D? Bouncer;

    private static PhysicsWorld3D Room()
    {
        var world = new PhysicsWorld3D(Bepu3DEngine.Instance, new Vector3(0, -9.81f, 0));
        world.AddStaticBox(new Vector3(0, -0.5f, 0), new Vector3(20, 0.5f, 20));
        return world;
    }

    private static int Run(PhysicsWorld3D world, ref long now, int frames, bool stopWhenIdle)
    {
        for (var i = 0; i < frames; i++)
        {
            now += Frame;
            if (!world.Step(new FrameTick(now, Frame)) && stopWhenIdle)
            {
                return i;
            }
        }

        return frames;
    }
}

public sealed class PhysicsWorld3DSlabTests
{
    [Fact]
    public void A_thin_slab_lying_on_the_floor_goes_to_sleep()
    {
        using var world = new PhysicsWorld3D(Bepu3DEngine.Instance, new System.Numerics.Vector3(0, -9.81f, 0));
        world.AddStaticBox(new System.Numerics.Vector3(0, -0.5f, 0), new System.Numerics.Vector3(20, 0.5f, 20));
        var slab = world.AddBox(new System.Numerics.Vector3(0.128f, 0.128f, 0.01f), new PhysicsPose3D(new System.Numerics.Vector3(0, 1, 0), System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, 0.3f)), new PhysicsMaterial(Density: 400, Friction: 0.5, Restitution: 0.25, GravityScale: 1, LinearDamping: 0.15));
        world.SetVelocity(slab, new System.Numerics.Vector3(1, 2, 5), new System.Numerics.Vector3(0.6f, 0.3f, 0));
        var now = 1_000_000_000L;
        var frames = 0;
        for (; frames < 900; frames++)
        {
            now += 16_666_667;
            if (!world.Step(new FrameTick(now, 16_666_667)))
            {
                break;
            }

            if (frames % 60 == 0)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"{frames} v={slab.Velocity} w={slab.AngularVelocity} y={slab.Pose.Position.Y}");
            }
        }

        Assert.True(frames < 900, "the slab never slept");
    }
}
