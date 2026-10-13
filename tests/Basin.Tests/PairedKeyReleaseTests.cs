using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PairedKeyReleaseTests
{
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int LibcClose(int fd);

    private static (CompositorTestHost Host, List<string> Keys, List<uint> EnterKeys) Focused()
    {
        var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var window = MappedToplevel.Map(host, host.Client);
        var keyboard = host.Client.Seat!.GetKeyboard();
        var keys = new List<string>();
        var enterKeys = new List<uint>();
        keyboard.Keymap += (_, e) => LibcClose(e.Fd);
        keyboard.Key += (_, e) => keys.Add($"{e.Key} {(e.State == WlKeyboard.KeyState.Pressed ? "down" : "up")}");
        keyboard.Enter += (_, e) =>
        {
            enterKeys.Clear();
            for (var i = 0; i + 4 <= e.Keys.Length; i += 4)
            {
                enterKeys.Add(BitConverter.ToUInt32(e.Keys, i));
            }
        };
        host.PumpToClient();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();
        return (host, keys, enterKeys);
    }

    [Fact]
    public void A_release_whose_press_was_consumed_is_not_delivered()
    {
        var (host, keys, _) = Focused();
        using (host)
        {
            host.Seat.Keyboard.NotifyKeyConsumed(InputCodes.KeyS, true);
            Assert.True(host.Seat.Keyboard.WasConsumed(InputCodes.KeyS));
            Assert.False(host.Seat.Keyboard.TryNotifyKey(10, InputCodes.KeyS, false));
            host.PumpToClient();

            Assert.Empty(keys);
            Assert.False(host.Seat.Keyboard.WasConsumed(InputCodes.KeyS));
            Assert.DoesNotContain(InputCodes.KeyS, host.Seat.Keyboard.PressedKeys);
        }
    }

    [Fact]
    public void A_release_consumed_after_its_press_was_delivered_is_delivered_anyway()
    {
        var (host, keys, _) = Focused();
        using (host)
        {
            host.Seat.Keyboard.NotifyKey(10, InputCodes.KeyF, WlKeyboard.KeyState.Pressed);
            host.Seat.Keyboard.NotifyKeyConsumed(InputCodes.KeyF, false);
            host.PumpUntil(() => keys.Count == 2);

            Assert.Equal([$"{InputCodes.KeyF} down", $"{InputCodes.KeyF} up"], keys);
        }
    }

    [Fact]
    public void A_consumed_key_held_across_a_focus_change_is_not_in_the_enter_list()
    {
        var (host, _, enterKeys) = Focused();
        using (host)
        {
            var other = MappedToplevel.Map(host, host.Client);
            host.Seat.Keyboard.NotifyKeyConsumed(InputCodes.KeyLeftMeta, true);
            host.Seat.Keyboard.NotifyKey(10, InputCodes.KeyS, WlKeyboard.KeyState.Pressed);
            host.Seat.Keyboard.NotifyEnter(other.ServerSurface);
            host.PumpUntil(() => enterKeys.Count == 1);

            Assert.Equal([InputCodes.KeyS], enterKeys);
            Assert.True(host.Seat.Keyboard.WasConsumed(InputCodes.KeyLeftMeta));
        }
    }

    [Fact]
    public void A_delivered_press_clears_a_stale_consumed_entry()
    {
        var (host, keys, _) = Focused();
        using (host)
        {
            host.Seat.Keyboard.NotifyKeyConsumed(InputCodes.KeyS, true);
            host.Seat.Keyboard.NotifyKey(10, InputCodes.KeyS, WlKeyboard.KeyState.Pressed);
            Assert.True(host.Seat.Keyboard.TryNotifyKey(20, InputCodes.KeyS, false));
            host.PumpUntil(() => keys.Count == 2);

            Assert.Equal([$"{InputCodes.KeyS} down", $"{InputCodes.KeyS} up"], keys);
        }
    }
}
