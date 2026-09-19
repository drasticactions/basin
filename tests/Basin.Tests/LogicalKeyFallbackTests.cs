using Avalonia.Input;
using Basin.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class LogicalKeyFallbackTests
{
    [Theory]
    [InlineData(Key.Escape, 1u)]
    [InlineData(Key.Back, 14u)]
    [InlineData(Key.Tab, 15u)]
    [InlineData(Key.Return, 28u)]
    [InlineData(Key.Space, 57u)]
    [InlineData(Key.Left, 105u)]
    [InlineData(Key.Delete, 111u)]
    public void The_keys_a_soft_keyboard_sends_without_a_scan_code_map_to_evdev(Key key, uint evdev) =>
        Assert.Equal(evdev, AvaloniaKeyMap.EvdevFor(key));

    [Fact]
    public void A_printable_key_has_no_logical_fallback_because_it_arrives_as_text() =>
        Assert.Equal(0u, AvaloniaKeyMap.EvdevFor(Key.A));
}
