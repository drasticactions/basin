using Basin.Backend.Headless;
using Basin.Seat;
using Wayland;
using Xkb;
using Xunit;

namespace Basin.Tests;

public sealed class HostSeatTests
{
    [Fact]
    public void Every_position_the_map_carries_is_a_distinct_evdev_code()
    {
        var seen = new HashSet<uint>();
        foreach (var (code, evdev) in HostKeyMap.Entries)
        {
            Assert.True(seen.Add(evdev), $"{code} repeats evdev {evdev}");
            Assert.True(HostKeyMap.TryToEvdev(code, out var forward));
            Assert.Equal(evdev, forward);
            Assert.True(HostKeyMap.TryFromEvdev(evdev, out var back));
            Assert.Equal(code, back);
        }

        Assert.False(HostKeyMap.TryToEvdev(HostKeyCode.None, out _));
    }

    [Fact]
    public void The_writing_keys_carry_the_evdev_codes_a_us_keymap_expects()
    {
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyQ, out var q));
        Assert.Equal(16u, q);
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyA, out var a));
        Assert.Equal(30u, a);
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.IntlBackslash, out var lsgt));
        Assert.Equal(86u, lsgt);
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.AltRight, out var altGr));
        Assert.Equal(100u, altGr);
    }

    [Fact]
    public void The_phantom_control_alongside_AltGr_is_masked()
    {
        var state = new HostModifierState { MasksPhantomControl = true };
        Span<HostKeyEvent> output = stackalloc HostKeyEvent[2];

        Assert.Equal(0, state.Feed(new HostKeyEvent(10, HostKeyCode.ControlLeft, true), output));

        var count = state.Feed(new HostKeyEvent(10, HostKeyCode.AltRight, true), output);
        Assert.Equal(1, count);
        Assert.Equal(HostKeyCode.AltRight, output[0].Code);
        Assert.Equal(HostModifiers.AltGr, state.Modifiers);
    }

    [Fact]
    public void A_control_press_that_no_AltGr_follows_still_arrives()
    {
        var state = new HostModifierState { MasksPhantomControl = true };
        Span<HostKeyEvent> output = stackalloc HostKeyEvent[2];

        Assert.Equal(0, state.Feed(new HostKeyEvent(10, HostKeyCode.ControlLeft, true), output));

        var count = state.Feed(new HostKeyEvent(400, HostKeyCode.KeyC, true), output);
        Assert.Equal(2, count);
        Assert.Equal(HostKeyCode.ControlLeft, output[0].Code);
        Assert.Equal(HostKeyCode.KeyC, output[1].Code);
        Assert.Equal(HostModifiers.Control, state.Modifiers);
    }

    [Fact]
    public void An_idle_host_releases_the_control_press_it_held()
    {
        var state = new HostModifierState { MasksPhantomControl = true };
        Span<HostKeyEvent> output = stackalloc HostKeyEvent[2];

        Assert.Equal(0, state.Feed(new HostKeyEvent(10, HostKeyCode.ControlLeft, true), output));
        Assert.Equal(0, state.Idle(20, output));
        Assert.Equal(1, state.Idle(500, output));
        Assert.Equal(HostKeyCode.ControlLeft, output[0].Code);
        Assert.Equal(HostModifiers.Control, state.Modifiers);
    }

    [Fact]
    public void A_host_without_the_mask_passes_control_straight_through()
    {
        var state = new HostModifierState { MasksPhantomControl = false };
        Span<HostKeyEvent> output = stackalloc HostKeyEvent[2];

        Assert.Equal(1, state.Feed(new HostKeyEvent(10, HostKeyCode.ControlLeft, true), output));
        Assert.Equal(HostModifiers.Control, state.Modifiers);
    }

    [Fact]
    public void An_injected_key_reaches_the_client_as_the_right_evdev_code()
    {
        using var host = new CompositorTestHost();
        var backend = new HeadlessBackend(host.Loop);
        var keyboard = backend.CreateKeyboard();
        keyboard.Key += (timeMs, evdev, pressed) => host.Seat.Keyboard.NotifyKey(timeMs, evdev, pressed);

        var window = MappedToplevel.Map(host, host.Client);
        var wlKeyboard = host.Client.Seat!.GetKeyboard();
        var keys = new List<(uint Key, uint State)>();
        wlKeyboard.Key += (_, e) => keys.Add((e.Key, (uint)e.State));
        host.PumpToClient();

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        keyboard.InjectKey(10, HostKeyCode.KeyQ, pressed: true);
        keyboard.InjectKey(20, HostKeyCode.KeyQ, pressed: false);
        host.PumpToClient();

        Assert.Equal([(16u, 1u), (16u, 0u)], keys);

        wlKeyboard.Dispose();
    }

    [Fact]
    public void A_client_resolves_the_injected_key_through_the_keymap_it_was_sent()
    {
        using var host = new CompositorTestHost();
        using var source = new HostKeymapSource(new FakeLayout());
        Assert.True(source.TryCompile(out var keymap));

        using var context = XkbContext.Create();
        using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(keymap.Text));
        Assert.NotNull(compiled);
        using var state = compiled!.CreateState();

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyQ, out var q));
        Assert.Equal("a", state.GetKeyString(q + 8));

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyW, out var w));
        Assert.Equal("z", state.GetKeyString(w + 8));

        keymap.Dispose();
    }

    [Fact]
    public void A_capture_failure_serves_the_embedded_map_rather_than_throwing()
    {
        using var source = new HostKeymapSource(new FailingLayout());
        Assert.True(source.TryCompile(out var keymap));
        Assert.False(source.ReadFromHost);

        using var context = XkbContext.Create();
        using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(keymap.Text));
        Assert.NotNull(compiled);
        using var state = compiled!.CreateState();
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyQ, out var q));
        Assert.Equal("q", state.GetKeyString(q + 8));
        keymap.Dispose();
    }

    [Fact]
    public void A_layout_change_asks_for_the_keymap_to_be_announced_again()
    {
        var layout = new FakeLayout();
        using var source = new HostKeymapSource(layout);
        var changed = 0;
        source.Changed += () => changed++;

        layout.RaiseChanged();
        Assert.Equal(1, changed);
    }

    [Fact]
    public void The_written_keymap_declares_level_three_only_when_a_key_uses_it()
    {
        var flat = HostKeymapWriter.Write("flat",
        [
            new HostKeymapWriter.Levels(HostKeyCode.KeyQ, "a", "A", null, null),
        ]);
        Assert.DoesNotContain("modifier_map Mod5", flat, StringComparison.Ordinal);

        var deep = HostKeymapWriter.Write("deep",
        [
            new HostKeymapWriter.Levels(HostKeyCode.KeyE, "e", "E", "EuroSign", null),
        ]);
        Assert.Contains("ISO_Level3_Shift", deep, StringComparison.Ordinal);
        Assert.Contains("modifier_map Mod5", deep, StringComparison.Ordinal);
    }

    [Fact]
    public void The_emitted_keymap_matches_its_golden_and_xkbcommon_compiles_it()
    {
        var text = HostKeymapWriter.Write("synthetic",
        [
            new HostKeymapWriter.Levels(HostKeyCode.Backquote, "grave", "asciitilde", null, null),
            new HostKeymapWriter.Levels(HostKeyCode.Digit1, "1", "exclam", "onesuperior", "exclamdown"),
            new HostKeymapWriter.Levels(HostKeyCode.KeyQ, "a", "A", "ae", "AE"),
            new HostKeymapWriter.Levels(HostKeyCode.KeyE, "e", "E", "EuroSign", null),
            new HostKeymapWriter.Levels(HostKeyCode.BracketLeft, "dead_acute", "dead_diaeresis", null, null),
            new HostKeymapWriter.Levels(HostKeyCode.IntlBackslash, "less", "greater", "bar", null),
            new HostKeymapWriter.Levels(HostKeyCode.Space, "space", "space", "nobreakspace", null),
        ]);

        var goldenPath = Path.Combine(GoldenDirectory(), "host-keymap-synthetic.xkb");
        if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(goldenPath, text);
        }

        Assert.True(File.Exists(goldenPath), $"golden missing; run with BASIN_UPDATE_GOLDENS=1 to write {goldenPath}");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), text.ReplaceLineEndings("\n"));

        using var context = XkbContext.Create();
        using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(text));
        Assert.NotNull(compiled);
    }

    private static string GoldenDirectory([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens");

    [Fact]
    public void An_azerty_layout_types_its_own_characters_and_its_AltGr_level()
    {
        using var context = XkbContext.Create();
        var text = HostKeymapWriter.Write("azerty",
        [
            new HostKeymapWriter.Levels(HostKeyCode.KeyQ, "a", "A", null, null),
            new HostKeymapWriter.Levels(HostKeyCode.KeyE, "e", "E", "EuroSign", null),
            new HostKeymapWriter.Levels(HostKeyCode.Digit2, "eacute", "2", "asciitilde", null),
        ]);

        using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(text));
        Assert.NotNull(compiled);
        using var state = compiled!.CreateState();

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.Digit2, out var two));
        Assert.Equal("é", state.GetKeyString(two + 8));

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.AltRight, out var altGr));
        state.UpdateKey(altGr + 8, XkbKeyDirection.Down);
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyE, out var e));
        Assert.Equal("€", state.GetKeyString(e + 8));
        state.UpdateKey(altGr + 8, XkbKeyDirection.Up);

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.ShiftLeft, out var shift));
        state.UpdateKey(shift + 8, XkbKeyDirection.Down);
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.KeyQ, out var q));
        Assert.Equal("A", state.GetKeyString(q + 8));
        state.UpdateKey(shift + 8, XkbKeyDirection.Up);
        Assert.Equal("a", state.GetKeyString(q + 8));
    }

    [Fact]
    public void A_jis_layout_writes_the_yen_and_ro_keys()
    {
        using var context = XkbContext.Create();
        var text = HostKeymapWriter.Write("jis",
        [
            new HostKeymapWriter.Levels(HostKeyCode.IntlYen, "yen", "bar", null, null),
            new HostKeymapWriter.Levels(HostKeyCode.IntlRo, "backslash", "underscore", null, null),
            new HostKeymapWriter.Levels(HostKeyCode.Digit2, "2", "quotedbl", null, null),
        ]);

        using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(text));
        Assert.NotNull(compiled);
        using var state = compiled!.CreateState();

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.IntlYen, out var yen));
        Assert.Equal("¥", state.GetKeyString(yen + 8));
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.ShiftLeft, out var shift));
        state.UpdateKey(shift + 8, XkbKeyDirection.Down);
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.Digit2, out var two));
        Assert.Equal("\"", state.GetKeyString(two + 8));
        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.IntlRo, out var ro));
        Assert.Equal("_", state.GetKeyString(ro + 8));
        Assert.Equal("|", state.GetKeyString(yen + 8));
        state.UpdateKey(shift + 8, XkbKeyDirection.Up);
        Assert.Equal("¥", state.GetKeyString(yen + 8));
    }

    [Fact]
    public void A_dead_key_composes_the_character_the_layout_promises()
    {
        using var context = XkbContext.Create();
        var text = HostKeymapWriter.Write("deadkeys",
        [
            new HostKeymapWriter.Levels(HostKeyCode.BracketLeft, "dead_acute", "dead_diaeresis", null, null),
            new HostKeymapWriter.Levels(HostKeyCode.KeyE, "e", "E", null, null),
        ]);

        using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(text));
        Assert.NotNull(compiled);
        using var state = compiled!.CreateState();

        Assert.True(HostKeyMap.TryToEvdev(HostKeyCode.BracketLeft, out var bracket));
        Assert.Equal("dead_acute", state.GetKeyOneSym(bracket + 8).Name);
    }

    [Fact]
    public void Every_accent_a_host_reports_for_a_dead_key_names_a_keysym_xkbcommon_knows()
    {
        foreach (var accent in "`'\"^~´¨ˆ˜ˋˊ¯ˉ˘˙˚°˝ˇ¸˛")
        {
            var name = HostKeymapWriter.DeadKeysymName(accent);
            Assert.False(
                XkbKeysym.FromName(name).IsNone,
                $"U+{(int)accent:X4} is written {name}, which xkbcommon drops from the keymap");
        }

        Assert.Equal("U2603", HostKeymapWriter.DeadKeysymName('☃'));
    }

    [Fact]
    public void The_host_layout_reader_emits_a_keymap_xkbcommon_places_level_for_level()
    {
        SkipWhenNothingDrainsTheMainQueue();
        var layout = HostKeyboardLayout.Detect();
        Assert.SkipWhen(layout is null, "no host layout reader here; the session's own xkb rules say");

        try
        {
            Assert.True(layout!.TryReadKeymapText(out var text), $"{layout.Name} reported no keymap");
            TestContext.Current.TestOutputHelper?.WriteLine($"host layout {layout.Name}:\n{text}");

            using var context = XkbContext.Create();
            using var compiled = context.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(text));
            Assert.NotNull(compiled);

            var codes = ReadWrittenKeycodes(text);
            var written = ReadWrittenKeys(text);
            Assert.NotEmpty(written);

            foreach (var (name, symbols) in written)
            {
                Assert.True(codes.TryGetValue(name, out var keycode), $"<{name}> carries symbols and no keycode");

                for (var level = 0; level < symbols.Count; level++)
                {
                    if (symbols[level] == "NoSymbol")
                    {
                        continue;
                    }

                    var placed = compiled!.GetKeySymsByLevel(keycode, 0, (uint)level);
                    Assert.True(
                        placed.Length == 1,
                        $"<{name}> level {level + 1} was written {symbols[level]} and compiled to {placed.Length} keysyms");
                    Assert.Equal(XkbKeysym.FromName(symbols[level]).Value, placed[0].Value);
                }
            }
        }
        finally
        {
            (layout as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void The_host_layout_is_what_the_keymap_source_serves()
    {
        SkipWhenNothingDrainsTheMainQueue();
        var reader = HostKeyboardLayout.Detect();
        Assert.SkipWhen(reader is null, "no host layout reader here; the session's own xkb rules say");
        (reader as IDisposable)?.Dispose();

        using var source = new HostKeymapSource(
            blobs: new Wayland.Server.Shm.TokenBlobFactory(new Wayland.Server.FdSlotTable()));
        Assert.True(source.TryCompile(out var keymap));
        Assert.True(source.ReadFromHost, $"{source.LayoutName} fell back to the embedded us map");
        Assert.Contains(source.LayoutName, keymap.Text, StringComparison.Ordinal);
        keymap.Dispose();
    }

    private static List<(string Name, List<string> Symbols)> ReadWrittenKeys(string text)
    {
        var keys = new List<(string, List<string>)>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("key <", StringComparison.Ordinal))
            {
                continue;
            }

            var close = trimmed.IndexOf('>', StringComparison.Ordinal);
            var open = trimmed.LastIndexOf('[');
            var end = trimmed.LastIndexOf(']');
            if (close < 0 || open < 0 || end <= open)
            {
                continue;
            }

            var symbols = new List<string>();
            foreach (var symbol in trimmed[(open + 1)..end].Split(','))
            {
                symbols.Add(symbol.Trim());
            }

            keys.Add((trimmed[5..close], symbols));
        }

        return keys;
    }

    private static Dictionary<string, uint> ReadWrittenKeycodes(string text)
    {
        var codes = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('<'))
            {
                continue;
            }

            var close = trimmed.IndexOf('>', StringComparison.Ordinal);
            var equals = trimmed.IndexOf('=', StringComparison.Ordinal);
            var semicolon = trimmed.IndexOf(';', StringComparison.Ordinal);
            if (close < 0 || equals < close || semicolon < equals ||
                !uint.TryParse(trimmed[(equals + 1)..semicolon].Trim(), out var keycode))
            {
                continue;
            }

            codes[trimmed[1..close]] = keycode;
        }

        return codes;
    }

    [Fact]
    public void An_injected_pointer_reaches_the_client()
    {
        using var host = new CompositorTestHost();
        var backend = new HeadlessBackend(host.Loop);
        var pointer = backend.CreatePointer();

        var motions = new List<(double X, double Y)>();
        var buttons = new List<(uint Button, bool Pressed)>();
        pointer.Motion += (_, x, y) => motions.Add((x, y));
        pointer.Button += (_, button, pressed) => buttons.Add((button, pressed));

        pointer.InjectMotion(10, 30, 40);
        pointer.InjectButton(20, 0x110, pressed: true);
        pointer.InjectAxis(30, new PointerAxis(WlPointer.Axis.VerticalScroll, 10));
        pointer.InjectFrame();

        Assert.Equal([(30d, 40d)], motions);
        Assert.Equal([(0x110u, true)], buttons);
    }

    private sealed class FakeLayout : IHostKeyboardLayout
    {
        public string Name => "fake";

        public event Action? Changed;

        public void RaiseChanged() => Changed?.Invoke();

        public bool TryReadKeymapText(out string xkb)
        {
            xkb = HostKeymapWriter.Write(Name,
            [
                new HostKeymapWriter.Levels(HostKeyCode.KeyQ, "a", "A", null, null),
                new HostKeymapWriter.Levels(HostKeyCode.KeyW, "z", "Z", null, null),
            ]);
            return true;
        }
    }

    private static void SkipWhenNothingDrainsTheMainQueue()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.SkipWhen(
            pthread_main_np() == 0 && objc_getClass("NSApplication") != 0,
            "AppKit is loaded and this process never runs the main queue, so the reader's deferred read cannot land");
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
    private static extern int pthread_main_np();

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint objc_getClass(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string name);

    private sealed class FailingLayout : IHostKeyboardLayout
    {
        public string Name => "failing";

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool TryReadKeymapText(out string xkb)
        {
            xkb = string.Empty;
            return false;
        }
    }
}
