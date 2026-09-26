using Xunit;

namespace BasinMcp.Tests;

public sealed class McpGlobTests
{
    [Fact]
    public void A_star_matches_within_one_part()
    {
        Assert.True(McpGlob.Parse("outputs/*").Matches("outputs/list"));
        Assert.True(McpGlob.Parse("*/list").Matches("windows/list"));
        Assert.True(McpGlob.Parse("windows/set-*").Matches("windows/set-state"));
        Assert.True(McpGlob.Parse("*/*").Matches("tinycomp/canvas"));
        Assert.True(McpGlob.Parse("session/quit").Matches("session/quit"));
        Assert.False(McpGlob.Parse("session/quit").Matches("session/describe"));
        Assert.Throws<FormatException>(() => McpGlob.Parse("out*"));
    }

    [Fact]
    public void A_pattern_without_one_slash_is_refused()
    {
        Assert.Throws<FormatException>(() => McpGlob.Parse("outputs"));
        Assert.Throws<FormatException>(() => McpGlob.Parse("a/b/c"));
        Assert.Throws<FormatException>(() => McpGlob.Parse("/list"));
        Assert.Equal(2, McpGlob.ParseList(" session/quit , outputs/* ").Count);
        Assert.Empty(McpGlob.ParseList(null));
    }
}
