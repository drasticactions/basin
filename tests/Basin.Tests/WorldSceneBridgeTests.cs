using System.Numerics;
using Basin.Scene3D;
using Basin.Scene3D.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class WorldSceneBridgeTests
{
    [Fact]
    public void The_centre_ray_looks_down_the_camera_and_an_empty_layout_has_none()
    {
        using var host = new CompositorTestHost(200, 100);
        var camera = new WorldCamera { Position = new Vector3(0, 1, 0) };
        Assert.True(camera.TryRayAt(host.Layout, 100, 50, out var ray));
        Assert.Equal(new Vector3(0, 1, 0), ray.Origin);
        Assert.True(ray.Direction.Z < -0.99f, $"the centre ray points {ray.Direction}");
        Assert.True(camera.TryRayAt(host.Layout, 900, 900, out _));
        Assert.False(camera.TryRayAt(new OutputLayout(), 10, 10, out _));
    }

    [Fact]
    public void A_frame_clone_is_a_separate_copy()
    {
        var frame = new WorldWindowFrame { Rail = 0.1f, PlaqueWidth = 0.3f, PlaquePixelWidth = 7 };
        var copy = frame.Clone();
        Assert.NotSame(frame, copy);
        Assert.Equal(0.1f, copy.Rail);
        Assert.Equal(7, copy.PlaquePixelWidth);
        copy.Rail = 0.2f;
        Assert.Equal(0.1f, frame.Rail);
    }

    [Fact]
    public void The_floor_is_due_once_a_second()
    {
        var callbacks = new WorldFrameCallbacks();
        Assert.True(callbacks.FloorDue(2_000_000_000));
        Assert.False(callbacks.FloorDue(2_500_000_000));
        Assert.True(callbacks.FloorDue(3_000_000_001));
    }
}
