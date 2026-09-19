using Basin.Diagnostics;
using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class KeyTableTests
{
    private sealed class LogCapture : IBasinLogSink, IDisposable
    {
        private readonly IBasinLogSink? _previousSink = BasinLog.Sink;
        private readonly BasinLogLevel _previousLevel = BasinLog.Level;

        public LogCapture()
        {
            BasinLog.Sink = this;
            BasinLog.Level = BasinLogLevel.Trace;
        }

        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) => Lines.Add($"{level}: {message}");

        public void Dispose()
        {
            BasinLog.Sink = _previousSink;
            BasinLog.Level = _previousLevel;
        }
    }

    private static KeyTable Build(IReadOnlyList<ShellKey> configured, IReadOnlyList<ShellChord>? reserved = null) =>
        KeyTable.Build(configured, reserved ?? []);

    private static KeyTable BuildCapturing(IReadOnlyList<ShellKey> configured, IReadOnlyList<ShellChord> reserved, out List<string> lines)
    {
        using var capture = new LogCapture();
        lines = capture.Lines;
        return KeyTable.Build(configured, reserved);
    }

    private static ShellKeyBinding Binding(KeyTable table, string name) =>
        Assert.Single(table.Bindings, binding => binding.Name == name);

    [Theory]
    [InlineData("switch-windows", "Alt+Tab", "Alt", 15u)]
    [InlineData("switch-windows-backward", "Shift+Alt+Tab", "Shift, Alt", 15u)]
    [InlineData("switch-group", "Alt+grave", "Alt", 41u)]
    [InlineData("cycle-windows", "Alt+Escape", "Alt", 1u)]
    [InlineData("switch-to-workspace-left", "Ctrl+Alt+Left", "Ctrl, Alt", 105u)]
    [InlineData("switch-to-workspace-right", "Ctrl+Alt+Right", "Ctrl, Alt", 106u)]
    [InlineData("switch-to-workspace-up", "Ctrl+Alt+Up", "Ctrl, Alt", 103u)]
    [InlineData("switch-to-workspace-down", "Ctrl+Alt+Down", "Ctrl, Alt", 108u)]
    [InlineData("move-to-workspace-left", "Ctrl+Shift+Alt+Left", "Ctrl, Shift, Alt", 105u)]
    [InlineData("move-to-workspace-right", "Ctrl+Shift+Alt+Right", "Ctrl, Shift, Alt", 106u)]
    [InlineData("move-to-workspace-up", "Ctrl+Shift+Alt+Up", "Ctrl, Shift, Alt", 103u)]
    [InlineData("move-to-workspace-down", "Ctrl+Shift+Alt+Down", "Ctrl, Shift, Alt", 108u)]
    [InlineData("show-desktop", "Ctrl+Alt+d", "Ctrl, Alt", 32u)]
    [InlineData("panel-main-menu", "Alt+F1", "Alt", 59u)]
    [InlineData("activate-window-menu", "Alt+space", "Alt", 57u)]
    [InlineData("toggle-maximized", "Alt+F10", "Alt", 68u)]
    [InlineData("unmaximize", "Alt+F5", "Alt", 63u)]
    [InlineData("minimize", "Alt+F9", "Alt", 67u)]
    [InlineData("close", "Alt+F4", "Alt", 62u)]
    [InlineData("begin-move", "Alt+F7", "Alt", 65u)]
    [InlineData("begin-resize", "Alt+F8", "Alt", 66u)]
    [InlineData("toggle-host-fullscreen", "Ctrl+Alt+Return", "Ctrl, Alt", 28u)]
    public void Every_key_in_the_table_has_its_default_chord(string name, string chord, string modifiers, uint code)
    {
        var held = Enum.Parse<ShellModifiers>(modifiers);
        Assert.Equal(chord, ShellKeyNames.DefaultChord(name));
        Assert.Equal(new ShellKeyBinding(name, chord, held, code), Binding(Build([]), name));
        Assert.Equal(name, Build([]).Match(held, code));
    }

    [Theory]
    [InlineData("toggle-shaded")]
    [InlineData("toggle-above")]
    [InlineData("tile-to-side-e")]
    [InlineData("tile-to-side-w")]
    [InlineData("maximize")]
    [InlineData("raise")]
    [InlineData("lower")]
    [InlineData("move-to-workspace-1")]
    [InlineData("move-to-workspace-12")]
    [InlineData("switch-to-workspace-1")]
    [InlineData("switch-to-workspace-12")]
    [InlineData("toggle-on-all-workspaces")]
    [InlineData("toggle-fullscreen")]
    [InlineData("maximize-vertically")]
    [InlineData("maximize-horizontally")]
    [InlineData("move-to-corner-nw")]
    [InlineData("move-to-corner-se")]
    [InlineData("move-to-side-n")]
    [InlineData("move-to-side-w")]
    [InlineData("move-to-center")]
    public void The_other_marco_names_are_known_and_off_by_default(string name)
    {
        Assert.True(ShellKeyNames.IsKnown(name));
        Assert.Null(ShellKeyNames.DefaultChord(name));
        Assert.DoesNotContain(Build([]).Bindings, binding => binding.Name == name);
    }

    [Theory]
    [InlineData("run-command-1")]
    [InlineData("panel-run-dialog")]
    [InlineData("take-screenshot")]
    [InlineData("switch-panels")]
    [InlineData("")]
    public void Names_outside_v1_are_unknown(string name)
    {
        Assert.False(ShellKeyNames.IsKnown(name));
        Assert.Null(ShellKeyNames.DefaultChord(name));
    }

    [Fact]
    public void The_default_table_has_no_duplicate_chords_and_every_binding_matches()
    {
        using var capture = new LogCapture();
        var table = KeyTable.Build([], []);

        Assert.DoesNotContain(capture.Lines, line => line.StartsWith("Warn", StringComparison.Ordinal));
        Assert.Equal(22, table.Bindings.Count);
        foreach (var binding in table.Bindings)
        {
            Assert.Equal(binding.Name, table.Match(binding.Modifiers, binding.Code));
        }

        Assert.Null(table.Match(ShellModifiers.None, 62));
        Assert.Null(table.Match(ShellModifiers.Alt | ShellModifiers.Ctrl, 62));
        Assert.Empty(KeyTable.Empty.Bindings);
        Assert.Null(KeyTable.Empty.Match(ShellModifiers.Alt, 62));
    }

    [Fact]
    public void A_configured_chord_replaces_the_default_and_turns_a_silent_key_on()
    {
        var table = Build([new ShellKey("close", "Super+q"), new ShellKey("tile-to-side-w", "Super+Left")]);

        Assert.Equal(new ShellKeyBinding("close", "Super+q", ShellModifiers.Super, 16), Binding(table, "close"));
        Assert.Equal(new ShellKeyBinding("tile-to-side-w", "Super+Left", ShellModifiers.Super, 105), Binding(table, "tile-to-side-w"));
        Assert.Null(table.Match(ShellModifiers.Alt, 62));
        Assert.Equal("close", table.Match(ShellModifiers.Super, 16));
    }

    [Fact]
    public void An_empty_chord_disables_the_key()
    {
        var table = Build([new ShellKey("close", ""), new ShellKey("minimize", "  ")]);

        Assert.DoesNotContain(table.Bindings, binding => binding.Name is "close" or "minimize");
        Assert.Null(table.Match(ShellModifiers.Alt, 62));
        Assert.Null(table.Match(ShellModifiers.Alt, 67));
    }

    [Fact]
    public void A_chord_naming_no_key_is_reported_once_and_dropped()
    {
        var table = BuildCapturing([new ShellKey("close", "Alt+Bogus")], [], out var lines);

        Assert.Equal(["Warn: shell key close: 'Alt+Bogus' names no key"], lines.Where(line => line.StartsWith("Warn", StringComparison.Ordinal)));
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "close");
    }

    [Fact]
    public void A_chord_with_an_unknown_modifier_is_reported_and_dropped()
    {
        var table = BuildCapturing([new ShellKey("close", "Hyper+F4")], [], out var lines);

        Assert.Contains("Warn: shell key close: 'Hyper+F4' has no modifier named 'Hyper'; the modifiers are shift, ctrl, alt and super", lines);
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "close");
    }

    [Fact]
    public void A_duplicate_chord_is_reported_once_and_the_first_name_keeps_it()
    {
        var table = BuildCapturing([new ShellKey("minimize", "alt+f4")], [], out var lines);

        Assert.Equal(
            ["Warn: shell key close and minimize both use alt+f4, keeping close"],
            lines.Where(line => line.StartsWith("Warn", StringComparison.Ordinal)).ToArray());
        Assert.Equal("close", table.Match(ShellModifiers.Alt, 62));
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "minimize");
    }

    [Fact]
    public void A_reserved_chord_wins_and_is_reported_once()
    {
        var reserved = new[] { new ShellChord(ShellModifiers.Alt, 62) };
        var table = BuildCapturing([], reserved, out var lines);

        Assert.Equal(
            ["Warn: shell key close uses Alt+F4, which the host already binds, keeping the host's"],
            lines.Where(line => line.StartsWith("Warn", StringComparison.Ordinal)));
        Assert.Null(table.Match(ShellModifiers.Alt, 62));
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "close");
        Assert.Equal("minimize", table.Match(ShellModifiers.Alt, 67));
    }

    [Fact]
    public void An_unknown_name_in_the_configured_list_is_ignored()
    {
        var table = Build([new ShellKey("explode", "Alt+F4")]);

        Assert.Equal("close", table.Match(ShellModifiers.Alt, 62));
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "explode");
    }

    [Theory]
    [InlineData("Alt+F4", "Alt", 62u)]
    [InlineData("ctrl+shift+alt+Down", "Ctrl, Shift, Alt", 108u)]
    [InlineData("Super+grave", "Super", 41u)]
    [InlineData("option+backquote", "Alt", 41u)]
    [InlineData("control+Return", "Ctrl", 28u)]
    [InlineData("win+Escape", "Super", 1u)]
    [InlineData("logo+Tab", "Super", 15u)]
    [InlineData("Alt+Home", "Alt", 102u)]
    [InlineData("Alt+Prior", "Alt", 104u)]
    [InlineData("Alt+Next", "Alt", 109u)]
    [InlineData("Alt+bracketleft", "Alt", 26u)]
    [InlineData("F12", "None", 88u)]
    public void The_chord_grammar_is_the_hotkey_one_over_evdev_codes(string chord, string modifiers, uint code)
    {
        Assert.Null(KeyTable.ChordProblem(chord, out var parsedModifiers, out var parsedCode));
        Assert.Equal(Enum.Parse<ShellModifiers>(modifiers), parsedModifiers);
        Assert.Equal(code, parsedCode);
    }
}
