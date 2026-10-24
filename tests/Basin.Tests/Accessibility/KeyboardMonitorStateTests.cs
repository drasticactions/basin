using Basin.Accessibility;
using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class KeyboardMonitorStateTests
{
    private const uint Insert = 0xff63;
    private const uint ShiftL = 0xffe1;
    private const uint LetterH = 0x68;
    private const uint LetterA = 0x61;
    private const uint ShiftMask = 1;
    private const uint Delay = 400;

    private static KeyMonitorEvent Press(uint time, uint keysym, uint modifiers = 0) => new(time, 1, false, modifiers, keysym, 0, Delay);

    private static KeyMonitorEvent Release(uint time, uint keysym, uint modifiers = 0) => new(time, 1, true, modifiers, keysym, 0, Delay);

    [Fact]
    public void A_watcher_sees_every_key_and_blocks_none()
    {
        var state = new KeyboardMonitorState();
        state.Client(":1.5").Watched = true;
        var watchers = new List<string>();
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(10, LetterA), watchers));
        Assert.Equal([":1.5"], watchers);
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Release(20, LetterA), watchers));
        Assert.Equal([":1.5"], watchers);
    }

    [Fact]
    public void A_full_grab_blocks_each_press_and_its_release()
    {
        var state = new KeyboardMonitorState();
        state.Client(":1.5").Grabbed = true;
        var watchers = new List<string>();
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(10, LetterA), watchers));
        state.Client(":1.5").Grabbed = false;
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Release(20, LetterA), watchers));
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(30, LetterA), watchers));
    }

    [Fact]
    public void A_keystroke_grab_matches_its_keysym_and_exact_modifiers_only()
    {
        var state = new KeyboardMonitorState();
        state.SetKeyGrabs(":1.5", [], [(LetterH, ShiftMask)]);
        var watchers = new List<string>();
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(10, LetterH), watchers));
        Assert.Empty(watchers);
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Release(15, LetterH), watchers));
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(20, LetterH, ShiftMask), watchers));
        Assert.Equal([":1.5"], watchers);
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Release(25, LetterH, ShiftMask), watchers));
    }

    [Fact]
    public void A_grabbed_modifier_takes_the_keys_pressed_while_it_is_down()
    {
        var state = new KeyboardMonitorState();
        state.SetKeyGrabs(":1.5", [Insert], []);
        var watchers = new List<string>();
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(1000, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(1100, LetterH), watchers));
        Assert.Equal([":1.5"], watchers);
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Release(1150, LetterH), watchers));
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Release(1200, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(5000, LetterH), watchers));
        Assert.Empty(watchers);
    }

    [Fact]
    public void A_second_press_of_the_grabbed_modifier_within_the_repeat_delay_passes_with_its_release()
    {
        var state = new KeyboardMonitorState();
        state.SetKeyGrabs(":1.5", [Insert], []);
        var watchers = new List<string>();
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(1000, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Release(1050, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(1200, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Release(1250, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(9000, Insert), watchers));
    }

    [Fact]
    public void A_vanished_client_loses_its_grabs()
    {
        var state = new KeyboardMonitorState();
        state.SetKeyGrabs(":1.5", [Insert], [(LetterH, 0)]);
        state.Forget(":1.5");
        var watchers = new List<string>();
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(10, Insert), watchers));
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(20, LetterH), watchers));
        Assert.Empty(watchers);
        Assert.Equal(0, state.ClientCount);
    }

    [Fact]
    public void A_shift_held_with_the_grabbed_modifier_is_blocked_and_released_cleanly()
    {
        var state = new KeyboardMonitorState();
        state.SetKeyGrabs(":1.5", [Insert], []);
        var watchers = new List<string>();
        state.Process(Press(1000, Insert), watchers);
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Press(1010, ShiftL), watchers));
        state.Process(Release(1020, Insert), watchers);
        Assert.Equal(KeyMonitorVerdict.Block, state.Process(Release(1030, ShiftL), watchers));
        Assert.Equal(KeyMonitorVerdict.Pass, state.Process(Press(1040, ShiftL), watchers));
    }
}
