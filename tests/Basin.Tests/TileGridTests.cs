using EightWm;
using Xunit;

namespace Basin.Tests;

public sealed class TileGridTests
{
    private static Tile Make(string name, string size = "square", string group = "Main") => new()
    {
        Name = name,
        Exec = "true",
        Group = group,
        Size = size switch
        {
            "small" => TileSize.Small,
            "wide" => TileSize.Wide,
            "large" => TileSize.Large,
            _ => TileSize.Square,
        },
    };

    private static TileGrid Grid(params Tile[] tiles)
    {
        var grid = new TileGrid();
        foreach (var tile in tiles)
        {
            grid.Add(tile);
        }

        grid.Layout(availableHeight: 578);
        return grid;
    }

    [Theory]
    [InlineData("small", 70, 70)]
    [InlineData("square", 150, 150)]
    [InlineData("wide", 310, 150)]
    [InlineData("large", 310, 310)]
    public void A_tile_is_its_units_of_seventy_with_the_gaps_between_them(string size, int width, int height)
    {
        var tile = Make("a", size);
        Grid(tile);

        Assert.Equal(width, tile.Box.Width);
        Assert.Equal(height, tile.Box.Height);
    }

    [Fact]
    public void Tiles_stack_down_a_column_before_starting_the_next()
    {
        var first = Make("a");
        var second = Make("b");
        var third = Make("c");
        var fourth = Make("d");
        Grid(first, second, third, fourth);

        Assert.Equal(0, first.Box.X);
        Assert.Equal(0, first.Box.Y);
        Assert.Equal(0, second.Box.X);
        Assert.Equal(160, second.Box.Y);
        Assert.Equal(0, third.Box.X);
        Assert.Equal(320, third.Box.Y);
        Assert.Equal(160, fourth.Box.X);
        Assert.Equal(0, fourth.Box.Y);
    }

    [Fact]
    public void A_shorter_screen_holds_fewer_rows()
    {
        var grid = new TileGrid();
        grid.Add(Make("a"));
        grid.Layout(availableHeight: 240);

        Assert.Equal(3, grid.Rows);
    }

    [Fact]
    public void A_group_starts_where_the_last_one_ended_plus_the_group_gap()
    {
        var first = Make("a", group: "Main");
        var second = Make("b", group: "More");
        var grid = Grid(first, second);

        Assert.Equal(2, grid.Groups.Count);
        Assert.Equal(0, first.Box.X);
        Assert.Equal(150 + TileGrid.GroupGap, second.Box.X);
    }

    [Fact]
    public void The_grid_reports_the_width_of_everything_in_it()
    {
        var grid = Grid(Make("a", "wide"), Make("b", group: "More"));

        Assert.Equal(310 + TileGrid.GroupGap + 150, grid.Width);
    }

    [Fact]
    public void A_hit_test_finds_the_tile_under_the_point()
    {
        var first = Make("a");
        var second = Make("b");
        var grid = Grid(first, second);

        Assert.Same(first, grid.At(10, 10));
        Assert.Same(second, grid.At(10, 200));
        Assert.Null(grid.At(10, 155));
        Assert.Null(grid.At(2000, 10));
    }

    [Fact]
    public void The_grid_is_logical_and_carries_no_output_scale()
    {
        var tile = Make("a");
        var grid = new TileGrid();
        grid.Add(tile);
        grid.Layout(availableHeight: 578);

        Assert.Equal(150, tile.Box.Width);
        Assert.Equal(150, tile.Box.Height);
    }

    [Fact]
    public void A_tile_taller_than_the_screen_is_cut_to_the_rows_there_are()
    {
        var large = Make("a", "large");
        var grid = new TileGrid();
        grid.Add(large);
        grid.Layout(availableHeight: 240);

        Assert.Equal(3, grid.Rows);
        Assert.Equal(230, large.Box.Height);
    }

    [Fact]
    public void Clearing_the_grid_forgets_every_group()
    {
        var grid = Grid(Make("a"), Make("b", group: "More"));

        grid.Clear();

        Assert.Empty(grid.Groups);
    }
}
