using Basin.Portal.Client;
using Xkb;
using Xunit;

namespace Basin.Tests;

public sealed class ClientKeymapTests
{
    private const uint KeyQ = 16;
    private const uint Key1 = 2;
    private const uint ShiftMask = 1;
    private const uint CyrillicShortI = 0x6ca;
    private const uint Exclam = 0x21;

    private static string? Text(string layout)
    {
        using var context = XkbContext.Create();
        using var keymap = context.CreateKeymap(new XkbRuleNames { Layout = layout });
        return keymap?.AsString(XkbKeymapFormat.TextV1);
    }

    [Fact]
    public void The_lookups_follow_the_layout_group_and_the_modifiers()
    {
        var text = Text("us,ru");
        Assert.SkipWhen(text is null, "no xkb keymap could be compiled on this host");
        using var keymap = new ClientKeymap();
        keymap.Compile(text!);

        Assert.Equal((uint)'q', keymap.KeysymForKeycode(KeyQ));
        Assert.False(keymap.TryKeycodeForKeysym(CyrillicShortI, out _, out _));

        keymap.UpdateModifiers(0, 0, 0, 1);
        Assert.True(keymap.TryKeycodeForKeysym(CyrillicShortI, out var keycode, out _));
        Assert.Equal(KeyQ, keycode);
        Assert.Equal(CyrillicShortI, keymap.KeysymForKeycode(KeyQ));
        Assert.Equal(CyrillicShortI, keymap.RawKeysymForKeycode(KeyQ));

        keymap.UpdateModifiers(ShiftMask, 0, 0, 0);
        Assert.Equal(Exclam, keymap.KeysymForKeycode(Key1));
        Assert.Equal((uint)'1', keymap.RawKeysymForKeycode(Key1));
    }
}
