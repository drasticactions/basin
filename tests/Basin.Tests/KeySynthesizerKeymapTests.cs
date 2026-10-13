using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using Basin.Seat;
using Wayland;
using Xkb;
using Xunit;

namespace Basin.Tests;

public sealed class KeySynthesizerKeymapTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern nint pread(int fd, byte[] buffer, nuint count, long offset);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static string ReadKeymap(int fd, uint size)
    {
        var bytes = new byte[size];
        var got = pread(fd, bytes, size, 0);
        close(fd);
        return Encoding.UTF8.GetString(bytes, 0, (int)Math.Max(0, got)).TrimEnd('\0');
    }

    [Fact]
    public void Characters_the_keymap_lacks_are_typed_on_spare_keys_and_the_keymap_comes_back()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap(new KeymapNames(Layout: "us"));
        var window = MappedToplevel.Map(host, host.Client);
        var keyboard = host.Client.Seat!.GetKeyboard();
        using var context = XkbContext.Create();
        var keymaps = new List<XkbKeymap>();
        var events = new List<string>();
        var keysyms = new List<uint>();
        keyboard.Keymap += (_, e) =>
        {
            var compiled = context.CreateKeymapFromBuffer(Encoding.UTF8.GetBytes(ReadKeymap(e.Fd, e.Size)), XkbKeymapFormat.TextV1);
            keymaps.Add(compiled!);
            events.Add("keymap");
        };
        keyboard.Key += (_, e) =>
        {
            if (e.State != WlKeyboard.KeyState.Pressed)
            {
                return;
            }

            var syms = keymaps[^1].GetKeySymsByLevel(e.Key + 8, 0, 0);
            keysyms.Add(syms.Length > 0 ? syms[0].Value : 0);
            events.Add("key");
        };
        host.PumpToClient();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => events.Count > 0);
        events.Clear();

        var synthesizer = new KeySynthesizer(host.Seat.Keyboard) { Keymap = host.Seat.Keyboard.Keymap };
        Assert.Equal(4, synthesizer.Type("aé€한", 10));
        host.PumpUntil(() => events.Count == 6);

        Assert.Equal(["keymap", "key", "key", "key", "key", "keymap"], events);
        Assert.Equal([0x61u, 0xe9u, 0x10020acu, 0x100d55cu], keysyms);
        Assert.Equal(0x61u, keymaps[^1].GetKeySymsByLevel(38, 0, 0)[0].Value);
        Assert.DoesNotContain(0xe9u, keymaps[^1].GetKeySymsByLevel(TextTyper.SpareKeycodes[0] + 8, 0, 0).ToArray().Select(s => s.Value));
        foreach (var keymap in keymaps)
        {
            keymap.Dispose();
        }
    }

    [Fact]
    public void An_edited_keymap_places_each_keysym_on_its_spare_key()
    {
        using var context = XkbContext.Create();
        using var us = context.CreateKeymap(new XkbRuleNames { Layout = "us" });
        Assert.NotNull(us);
        var text = us!.AsString(XkbKeymapFormat.TextV1);
        var edited = KeymapEdit.WithKeysyms(text, [(TextTyper.SpareKeycodes[0], 0xe9), (TextTyper.SpareKeycodes[11], 0x10020ac)]);
        Assert.NotNull(edited);
        using var compiled = context.CreateKeymapFromBuffer(Encoding.UTF8.GetBytes(edited!), XkbKeymapFormat.TextV1);
        Assert.NotNull(compiled);
        Assert.Equal(0xe9u, compiled!.GetKeySymsByLevel(TextTyper.SpareKeycodes[0] + 8, 0, 0)[0].Value);
        Assert.Equal(0x10020acu, compiled.GetKeySymsByLevel(TextTyper.SpareKeycodes[11] + 8, 0, 0)[0].Value);
        Assert.Equal(0x61u, compiled.GetKeySymsByLevel(38, 0, 0)[0].Value);
    }
}
