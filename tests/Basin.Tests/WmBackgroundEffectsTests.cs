using Basin.Desktop;
using Xunit;

namespace Basin.Tests;

public sealed class WmBackgroundEffectsTests
{
    [Fact]
    public void Without_the_global_the_binding_says_so_and_does_nothing()
    {
        using var fixture = new RiverFixture();
        var effects = fixture.Client.BackgroundEffects;

        Assert.False(effects.Bound);
        Assert.False(effects.Supported);
        var surface = fixture.Client.Compositor!.CreateSurface();
        Assert.False(effects.SetBlurRegion(surface, [new Box(0, 0, 10, 10)]));
        surface.Destroy();
    }

    [Fact]
    public void A_global_with_no_blur_is_bound_but_not_supported()
    {
        BackgroundEffectManager? manager = null;
        using var fixture = new RiverFixture(beforeConnect: host =>
            manager = new BackgroundEffectManager(host.Display, host.Compositor, effects: null));
        fixture.Settle();

        Assert.True(fixture.Client.BackgroundEffects.Bound);
        Assert.False(fixture.Client.BackgroundEffects.Supported);
        manager!.Dispose();
    }

    [Fact]
    public void A_region_set_through_the_binding_reaches_the_surface_on_commit_and_clears()
    {
        BackgroundEffectManager? manager = null;
        using var fixture = new RiverFixture(beforeConnect: host =>
            manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable()));
        fixture.Settle();
        var effects = fixture.Client.BackgroundEffects;
        Assert.True(effects.Supported);

        var surface = fixture.Client.Compositor!.CreateSurface();
        fixture.Settle();
        var server = fixture.Host.SurfaceScenes[^1].Surface;

        using (var rounded = new Pixman.PixmanRegion32())
        {
            RoundedRegion.Fill(rounded, 40, 20, 6, RoundedCorners.Top);
            Assert.True(effects.SetBlurRegion(surface, rounded));
        }

        surface.Commit();
        fixture.Settle();
        var region = manager!.BlurRegionOf(server)!;
        Assert.Equal((0, 0, 40, 20), (region.Extents.X1, region.Extents.Y1, region.Extents.X2, region.Extents.Y2));
        Assert.False(region.Contains(0, 0));
        Assert.True(region.Contains(20, 0));

        Assert.True(effects.SetBlurRegion(surface, [new Box(2, 3, 10, 5)]));
        surface.Commit();
        fixture.Settle();
        region = manager.BlurRegionOf(server)!;
        Assert.Equal((2, 3, 12, 8), (region.Extents.X1, region.Extents.Y1, region.Extents.X2, region.Extents.Y2));

        Assert.True(effects.SetBlurRegion(surface, ReadOnlySpan<Box>.Empty));
        surface.Commit();
        fixture.Settle();
        Assert.True(manager.BlurRegionOf(server)!.IsEmpty);

        effects.SetBlurRegion(surface, [new Box(0, 0, 5, 5)]);
        surface.Commit();
        fixture.Settle();
        effects.Forget(surface);
        surface.Commit();
        fixture.Settle();
        Assert.True(manager.BlurRegionOf(server)!.IsEmpty);

        surface.Destroy();
        fixture.Settle();
        manager.Dispose();
    }

    private sealed class BlurCapable : Basin.Capabilities.IBackgroundEffects
    {
        public Basin.Capabilities.BackgroundEffects Supported => Basin.Capabilities.BackgroundEffects.Blur;
    }
}
