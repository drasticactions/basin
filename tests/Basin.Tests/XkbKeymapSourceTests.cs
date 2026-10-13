using Basin.Capabilities;
using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class XkbKeymapSourceTests
{
    [Fact]
    public void An_unknown_layout_or_a_broken_keymap_returns_false_rather_than_throwing()
    {
        using var source = new XkbKeymapSource();
        Assert.False(source.TryCompile(new KeymapNames(Layout: "no-such-layout"), out _));
        Assert.False(source.TryCompile("xkb_keymap { this is not a keymap", out _));
        Assert.True(source.TryCompile(new KeymapNames(Layout: "de", Variant: "neo"), out var keymap));
        keymap.Dispose();
    }
}
