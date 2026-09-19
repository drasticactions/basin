using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class WorkspacesTests
{
    private static Workspaces Grid(int count, int rows, int current = 0)
    {
        var workspaces = new Workspaces(count, rows, []);
        workspaces.Switch(current);
        return workspaces;
    }

    [Fact]
    public void Given_names_come_first_and_the_rest_are_numbered()
    {
        var workspaces = new Workspaces(4, 1, ["Main", "Mail"]);

        Assert.Equal(4, workspaces.Count);
        Assert.Equal(1, workspaces.Rows);
        Assert.Equal(4, workspaces.Columns);
        Assert.Equal(0, workspaces.Current);
        Assert.Equal(["Main", "Mail", "3", "4"], workspaces.Names);
    }

    [Fact]
    public void Rows_are_capped_at_the_count_and_columns_round_up()
    {
        Assert.Equal(2, new Workspaces(2, 4, []).Rows);
        Assert.Equal(1, new Workspaces(2, 4, []).Columns);
        Assert.Equal(3, new Workspaces(7, 3, []).Columns);
        Assert.Equal(1, new Workspaces(0, 0, []).Count);
    }

    [Fact]
    public void Switch_moves_the_current_index_and_rejects_one_outside()
    {
        var workspaces = new Workspaces(4, 1, []);

        workspaces.Switch(2);

        Assert.Equal(2, workspaces.Current);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspaces.Switch(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspaces.Switch(-1));
    }

    [Theory]
    [InlineData(0, WorkspaceDirection.Left, null)]
    [InlineData(0, WorkspaceDirection.Right, 1)]
    [InlineData(0, WorkspaceDirection.Up, null)]
    [InlineData(0, WorkspaceDirection.Down, null)]
    [InlineData(3, WorkspaceDirection.Right, null)]
    [InlineData(3, WorkspaceDirection.Left, 2)]
    public void No_wrap_stops_at_the_edge_of_a_single_row(int current, WorkspaceDirection direction, int? expected)
    {
        Assert.Equal(expected, Grid(4, 1, current).Neighbor(direction));
    }

    [Theory]
    [InlineData(1, WorkspaceDirection.Right, WrapStyle.NoWrap, null)]
    [InlineData(1, WorkspaceDirection.Right, WrapStyle.Classic, 2)]
    [InlineData(1, WorkspaceDirection.Right, WrapStyle.Toroidal, 0)]
    [InlineData(2, WorkspaceDirection.Left, WrapStyle.NoWrap, null)]
    [InlineData(2, WorkspaceDirection.Left, WrapStyle.Classic, 1)]
    [InlineData(2, WorkspaceDirection.Left, WrapStyle.Toroidal, 3)]
    [InlineData(3, WorkspaceDirection.Down, WrapStyle.NoWrap, null)]
    [InlineData(3, WorkspaceDirection.Down, WrapStyle.Classic, 0)]
    [InlineData(3, WorkspaceDirection.Down, WrapStyle.Toroidal, 1)]
    [InlineData(0, WorkspaceDirection.Up, WrapStyle.NoWrap, null)]
    [InlineData(0, WorkspaceDirection.Up, WrapStyle.Classic, 3)]
    [InlineData(0, WorkspaceDirection.Up, WrapStyle.Toroidal, 2)]
    [InlineData(0, WorkspaceDirection.Down, WrapStyle.NoWrap, 2)]
    public void Wrap_styles_follow_marco_on_a_two_by_two_grid(int current, WorkspaceDirection direction, WrapStyle wrap, int? expected)
    {
        Assert.Equal(expected, Grid(4, 2, current).Neighbor(direction, wrap));
    }

    [Theory]
    [InlineData(3, WorkspaceDirection.Right, WrapStyle.Classic, 0)]
    [InlineData(3, WorkspaceDirection.Right, WrapStyle.Toroidal, 0)]
    [InlineData(0, WorkspaceDirection.Left, WrapStyle.Classic, 3)]
    [InlineData(0, WorkspaceDirection.Left, WrapStyle.Toroidal, 3)]
    [InlineData(0, WorkspaceDirection.Up, WrapStyle.Toroidal, null)]
    [InlineData(0, WorkspaceDirection.Up, WrapStyle.Classic, 3)]
    public void Wrap_styles_on_a_single_row(int current, WorkspaceDirection direction, WrapStyle wrap, int? expected)
    {
        Assert.Equal(expected, Grid(4, 1, current).Neighbor(direction, wrap));
    }

    [Theory]
    [InlineData(6, WorkspaceDirection.Right, WrapStyle.NoWrap, null)]
    [InlineData(6, WorkspaceDirection.Right, WrapStyle.Toroidal, null)]
    [InlineData(6, WorkspaceDirection.Right, WrapStyle.Classic, 0)]
    [InlineData(6, WorkspaceDirection.Left, WrapStyle.Toroidal, null)]
    [InlineData(6, WorkspaceDirection.Left, WrapStyle.Classic, 5)]
    [InlineData(4, WorkspaceDirection.Down, WrapStyle.NoWrap, null)]
    [InlineData(4, WorkspaceDirection.Down, WrapStyle.Toroidal, 1)]
    [InlineData(4, WorkspaceDirection.Down, WrapStyle.Classic, 2)]
    [InlineData(1, WorkspaceDirection.Up, WrapStyle.Toroidal, 4)]
    [InlineData(3, WorkspaceDirection.Down, WrapStyle.NoWrap, 6)]
    public void An_uneven_grid_never_lands_on_a_missing_cell(int current, WorkspaceDirection direction, WrapStyle wrap, int? expected)
    {
        Assert.Equal(expected, Grid(7, 3, current).Neighbor(direction, wrap));
    }

    [Fact]
    public void Shrinking_reports_the_removed_indices_and_clamps_the_current_one()
    {
        var workspaces = new Workspaces(4, 1, ["Main"]);
        workspaces.Switch(3);

        var removed = workspaces.Resize(2);

        Assert.Equal(2..4, removed);
        Assert.Equal(2, workspaces.Count);
        Assert.Equal(1, workspaces.Current);
        Assert.Equal(["Main", "2"], workspaces.Names);
    }

    [Fact]
    public void Growing_removes_nothing_and_numbers_the_new_ones()
    {
        var workspaces = new Workspaces(2, 1, ["Main"]);
        workspaces.Switch(1);

        var removed = workspaces.Resize(5);

        Assert.Equal(5..5, removed);
        Assert.Equal(1, workspaces.Current);
        Assert.Equal(["Main", "2", "3", "4", "5"], workspaces.Names);
        Assert.Equal(5, workspaces.Columns);
    }

    [Fact]
    public void Resizing_below_one_keeps_one_workspace()
    {
        var workspaces = new Workspaces(3, 1, []);
        workspaces.Switch(2);

        var removed = workspaces.Resize(0);

        Assert.Equal(1..3, removed);
        Assert.Equal(1, workspaces.Count);
        Assert.Equal(0, workspaces.Current);
    }
}
