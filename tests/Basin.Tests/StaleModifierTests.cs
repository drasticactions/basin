using Avalonia.Input;
using Basin.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class StaleModifierTests
{
    [Fact]
    public void Modifiers_the_host_no_longer_reports_are_stale()
    {
        var pressed = new HashSet<uint> { 42, 125, 30 };

        var stale = AvaloniaKeyMap.StaleModifiers(KeyModifiers.None, pressed);

        Assert.Equal([42u, 125u], stale);
    }

    [Fact]
    public void Modifiers_the_host_still_reports_are_kept()
    {
        var pressed = new HashSet<uint> { 42, 54, 29 };

        var stale = AvaloniaKeyMap.StaleModifiers(KeyModifiers.Shift, pressed);

        Assert.Equal([29u], stale);
    }

    [Fact]
    public void Nothing_stale_returns_null()
    {
        var pressed = new HashSet<uint> { 30 };

        Assert.Null(AvaloniaKeyMap.StaleModifiers(KeyModifiers.None, pressed));
    }
}
