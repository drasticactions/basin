using Xunit;

namespace TabWm.Tests;

public sealed class PlacementTests
{
    private sealed record FakeTab(string Name);

    private static readonly FakeTab Parent = new("parent");
    private static readonly FakeTab Token = new("token");
    private static readonly FakeTab Pid = new("pid");
    private static readonly FakeTab Active = new("active");

    [Fact]
    public void Every_rule_present_picks_the_parent() =>
        Assert.Equal((Parent, PlacementVia.Parent), TabPlacement.Choose(Parent, Token, Pid, Active));

    [Fact]
    public void Without_a_parent_the_token_wins() =>
        Assert.Equal((Token, PlacementVia.Token), TabPlacement.Choose(null, Token, Pid, Active));

    [Fact]
    public void Without_a_parent_or_a_token_the_pid_wins() =>
        Assert.Equal((Pid, PlacementVia.Pid), TabPlacement.Choose(null, null, Pid, Active));

    [Fact]
    public void With_nothing_traced_the_active_tab_wins() =>
        Assert.Equal((Active, PlacementVia.Active), TabPlacement.Choose<FakeTab>(null, null, null, Active));

    [Fact]
    public void A_parent_beats_a_pid_even_without_a_token() =>
        Assert.Equal((Parent, PlacementVia.Parent), TabPlacement.Choose(Parent, null, Pid, Active));

    [Theory]
    [InlineData("Parent", "parent")]
    [InlineData("Token", "token")]
    [InlineData("Pid", "pid")]
    [InlineData("Active", "active")]
    public void Each_rule_prints_its_name(string via, string name) => Assert.Equal(name, TabPlacement.Name(Enum.Parse<PlacementVia>(via)));
}
