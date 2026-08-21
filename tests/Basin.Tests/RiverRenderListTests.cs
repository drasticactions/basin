using Basin.Shell.River;
using Xunit;

namespace Basin.Tests;

public class RiverRenderListTests
{
    [Fact]
    public void Place_above_matches_the_protocols_worked_example()
    {
        Assert.Equal("B,C,A", Apply((list, a, b, c) => list.PlaceAbove(a, c)));
        Assert.Equal("B,A,C", Apply((list, a, b, c) => list.PlaceAbove(a, b)));
        Assert.Equal("A,B,C", Apply((list, a, b, c) => list.PlaceAbove(b, a)));
    }

    [Fact]
    public void Place_below_matches_the_protocols_worked_example()
    {
        Assert.Equal("C,A,B", Apply((list, a, b, c) => list.PlaceBelow(c, a)));
        Assert.Equal("A,C,B", Apply((list, a, b, c) => list.PlaceBelow(c, b)));
        Assert.Equal("A,B,C", Apply((list, a, b, c) => list.PlaceBelow(b, c)));
    }

    [Fact]
    public void Place_top_and_bottom_move_to_the_ends()
    {
        Assert.Equal("B,C,A", Apply((list, a, b, c) => list.PlaceTop(a)));
        Assert.Equal("C,A,B", Apply((list, a, b, c) => list.PlaceBottom(c)));
    }

    [Fact]
    public void Placing_a_node_relative_to_itself_does_nothing()
    {
        Assert.Equal("A,B,C", Apply((list, a, b, c) => list.PlaceAbove(b, b)));
        Assert.Equal("A,B,C", Apply((list, a, b, c) => list.PlaceBelow(b, b)));
    }

    [Fact]
    public void New_entries_append_on_top()
    {
        var list = new RenderList<string>();
        list.Add("A");
        list.Add("B");
        Assert.Equal("A,B", string.Join(',', list.Entries));

        list.Add("A");
        Assert.Equal("A,B", string.Join(',', list.Entries));
    }

    [Fact]
    public void Removing_an_entry_leaves_the_rest_in_order()
    {
        var list = new RenderList<string>();
        list.Add("A");
        list.Add("B");
        list.Add("C");
        Assert.True(list.Remove("B"));
        Assert.False(list.Remove("B"));
        Assert.Equal("A,C", string.Join(',', list.Entries));
    }

    private static string Apply(Action<RenderList<string>, string, string, string> operation)
    {
        var list = new RenderList<string>();
        list.Add("A");
        list.Add("B");
        list.Add("C");
        operation(list, "A", "B", "C");
        return string.Join(',', list.Entries);
    }
}
