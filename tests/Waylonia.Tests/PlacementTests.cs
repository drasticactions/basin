using Avalonia;
using Basin.Avalonia;
using Basin.Shell.Xdg;
using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class PlacementTests
{
    [Fact]
    public void A_window_centers_on_the_screen_it_is_placed_on()
    {
        var placed = CursorScreenPolicy.Centered(new PixelRect(0, 0, 1920, 1080), 800, 600);

        Assert.Equal(new PixelPoint(560, 240), placed);
    }

    [Fact]
    public void A_second_screen_places_in_its_own_coordinates()
    {
        var placed = CursorScreenPolicy.Centered(new PixelRect(1920, -180, 2560, 1440), 800, 600);

        Assert.Equal(new PixelPoint(1920 + 880, -180 + 420), placed);
    }

    [Fact]
    public void A_window_wider_than_the_screen_starts_at_its_origin()
    {
        var placed = CursorScreenPolicy.Centered(new PixelRect(1920, 0, 1280, 720), 2000, 900);

        Assert.Equal(new PixelPoint(1920, 0), placed);
    }

    [Fact]
    public void A_screen_or_a_window_with_no_extent_is_left_to_the_platform()
    {
        Assert.Null(CursorScreenPolicy.Centered(new PixelRect(0, 0, 1920, 1080), 0, 600));
        Assert.Null(CursorScreenPolicy.Centered(new PixelRect(0, 0, 0, 0), 800, 600));
    }

    private static readonly HostScreenInfo[] TwoScreens =
    [
        new("built-in", "built-in", 0, 0, 1920, 1080, 1.0, true),
        new("external", "external", 1920, -180, 2560, 1440, 1.0, false),
    ];

    [Fact]
    public void A_layer_surface_takes_the_screen_the_pointer_is_over()
    {
        Assert.Equal("built-in", CursorScreenPolicy.Containing(TwoScreens, new PixelPoint(400, 300)));
        Assert.Equal("external", CursorScreenPolicy.Containing(TwoScreens, new PixelPoint(2400, 200)));
    }

    [Fact]
    public void A_pointer_on_a_screen_edge_belongs_to_the_screen_it_starts()
    {
        Assert.Equal("built-in", CursorScreenPolicy.Containing(TwoScreens, new PixelPoint(0, 0)));
        Assert.Equal("external", CursorScreenPolicy.Containing(TwoScreens, new PixelPoint(1920, 0)));
        Assert.Equal("built-in", CursorScreenPolicy.Containing(TwoScreens, new PixelPoint(1919, 1079)));
    }

    [Fact]
    public void A_pointer_on_no_screen_leaves_the_default_in_place()
    {
        Assert.Null(CursorScreenPolicy.Containing(TwoScreens, new PixelPoint(-40, -40)));
        Assert.Null(CursorScreenPolicy.Containing([], new PixelPoint(10, 10)));
    }

    [Fact]
    public void Each_layer_takes_its_own_host_stacking_band()
    {
        Assert.Equal(HostStackingBand.Background, HostStacking.BandFor(LayerKind.Background));
        Assert.Equal(HostStackingBand.Below, HostStacking.BandFor(LayerKind.Bottom));
        Assert.Equal(HostStackingBand.Above, HostStacking.BandFor(LayerKind.Top));
        Assert.Equal(HostStackingBand.Overlay, HostStacking.BandFor(LayerKind.Overlay));
    }

    [Fact]
    public void Only_the_layers_above_the_windows_are_topmost()
    {
        Assert.False(HostStacking.IsTopmost(HostStacking.BandFor(LayerKind.Background)));
        Assert.False(HostStacking.IsTopmost(HostStacking.BandFor(LayerKind.Bottom)));
        Assert.True(HostStacking.IsTopmost(HostStacking.BandFor(LayerKind.Top)));
        Assert.True(HostStacking.IsTopmost(HostStacking.BandFor(LayerKind.Overlay)));
    }
}
