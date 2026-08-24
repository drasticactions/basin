using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class DropShadowTests
{
    private static DropShadowOptions Large() => new()
    {
        Primary = new DropShadowLayer(0, 0, 48, 0.8),
        Secondary = new DropShadowLayer(0, -6, 24, 0.2),
        OffsetY = 12,
        CornerRadius = 5,
    };

    private static uint Pixel(DropShadowTexture texture, int x, int y)
    {
        var buffer = (MemoryBuffer)texture.Buffer;
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                return *(uint*)(view.Data + (y * view.Stride) + (x * 4));
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    [Fact]
    public void The_texture_is_a_nine_patch_with_a_single_pixel_centre()
    {
        using var texture = DropShadowTexture.Build(Large(), 1);
        Assert.NotNull(texture);
        Assert.Equal(273, texture.Width);
        Assert.Equal(273, texture.Height);
        Assert.Equal(new Box(136, 136, 1, 1), texture.Center);
        Assert.Equal(65, texture.PaddingLeft, 3);
        Assert.Equal(65, texture.PaddingRight, 3);
        Assert.Equal(53, texture.PaddingTop, 3);
        Assert.Equal(77, texture.PaddingBottom, 3);
    }

    [Fact]
    public void The_window_area_is_punched_out_and_the_outer_edge_fades_away()
    {
        using var texture = DropShadowTexture.Build(Large(), 1);
        Assert.NotNull(texture);

        Assert.Equal(0u, Pixel(texture, texture.Width / 2, texture.Height / 2) >> 24);
        Assert.Equal(0u, Pixel(texture, 0, texture.Height / 2) >> 24);

        var below = Pixel(texture, texture.Width / 2, texture.Height - (int)texture.PaddingBottom) >> 24;
        var above = Pixel(texture, texture.Width / 2, (int)texture.PaddingTop) >> 24;
        Assert.InRange(below, 1u, 255u);
        Assert.True(below > above, "the composite offset pushes the shadow downwards");
    }

    [Fact]
    public void The_shadow_is_darkest_at_the_window_edge_and_monotone_outwards()
    {
        using var texture = DropShadowTexture.Build(Large(), 1);
        Assert.NotNull(texture);

        var column = texture.Width / 2;
        var edge = texture.Height - (int)texture.PaddingBottom;
        var last = 256u;
        for (var y = edge; y < texture.Height; y++)
        {
            var alpha = Pixel(texture, column, y) >> 24;
            Assert.True(alpha <= last, $"alpha rises again at row {y}");
            last = alpha;
        }

        Assert.Equal(0u, last);
    }

    [Fact]
    public void Strength_scales_every_layer()
    {
        using var full = DropShadowTexture.Build(Large(), 1);
        using var half = DropShadowTexture.Build(Large() with { Strength = 0.5 }, 1);
        Assert.NotNull(full);
        Assert.NotNull(half);

        var y = full.Height - (int)full.PaddingBottom;
        var strong = Pixel(full, full.Width / 2, y) >> 24;
        var weak = Pixel(half, half.Width / 2, y) >> 24;
        Assert.True(weak < strong);
        Assert.True(weak > 0);
    }

    [Fact]
    public void A_zero_opacity_shadow_builds_nothing()
    {
        var options = new DropShadowOptions
        {
            Primary = new DropShadowLayer(0, 0, 48, 0),
            Secondary = new DropShadowLayer(0, 0, 24, 0),
        };
        Assert.Null(DropShadowTexture.Build(options, 1));
    }

    [Fact]
    public void The_cells_tile_the_padded_rectangle_without_overlapping()
    {
        using var host = new CompositorTestHost();
        using var texture = DropShadowTexture.Build(Large(), 1);
        Assert.NotNull(texture);

        var window = new SceneTree(host.Scene.Root);
        using var shadow = new DropShadowEffect(window) { Texture = texture };
        shadow.SetGeometry(new Box(0, 0, 400, 300));

        var cells = shadow.Tree.Children.OfType<SceneBuffer>().Where(cell => cell.Enabled).ToList();
        Assert.Equal(8, cells.Count);

        var padded = new Box(
            -(int)texture.PaddingLeft,
            -(int)texture.PaddingTop,
            400 + (int)(texture.PaddingLeft + texture.PaddingRight),
            300 + (int)(texture.PaddingTop + texture.PaddingBottom));

        var area = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var first = BoxOf(cells[i]);
            Assert.Equal(first, first.Intersect(padded));
            area += first.Width * first.Height;
            for (var j = i + 1; j < cells.Count; j++)
            {
                Assert.True(first.Intersect(BoxOf(cells[j])).IsEmpty, $"{first} overlaps {BoxOf(cells[j])}");
            }
        }

        var centre = texture.Center.Width * texture.Center.Height;
        Assert.Equal((padded.Width * padded.Height) - ((padded.Width - 272) * (padded.Height - 272) * centre), area);
    }

    [Fact]
    public void A_window_below_five_pixels_carries_no_shadow()
    {
        using var host = new CompositorTestHost();
        using var texture = DropShadowTexture.Build(Large(), 1);
        Assert.NotNull(texture);

        var window = new SceneTree(host.Scene.Root);
        using var shadow = new DropShadowEffect(window) { Texture = texture };

        shadow.SetGeometry(new Box(0, 0, 4, 200));
        Assert.False(shadow.Tree.Enabled);

        shadow.SetGeometry(new Box(0, 0, 200, 200));
        Assert.True(shadow.Tree.Enabled);

        shadow.Visible = false;
        Assert.False(shadow.Tree.Enabled);
    }

    [Fact]
    public void A_small_window_splits_the_overlapping_corner_tiles()
    {
        using var host = new CompositorTestHost();
        using var texture = DropShadowTexture.Build(Large(), 1);
        Assert.NotNull(texture);

        var window = new SceneTree(host.Scene.Root);
        using var shadow = new DropShadowEffect(window) { Texture = texture };
        shadow.SetGeometry(new Box(0, 0, 20, 20));

        var cells = shadow.Tree.Children.OfType<SceneBuffer>().Where(cell => cell.Enabled).ToList();
        Assert.NotEmpty(cells);
        for (var i = 0; i < cells.Count; i++)
        {
            for (var j = i + 1; j < cells.Count; j++)
            {
                Assert.True(BoxOf(cells[i]).Intersect(BoxOf(cells[j])).IsEmpty);
            }
        }
    }

    private static Box BoxOf(SceneBuffer cell) =>
        new(cell.X, cell.Y, cell.DestinationWidth, cell.DestinationHeight);
}
